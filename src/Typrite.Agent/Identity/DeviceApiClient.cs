using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Typrite.Contracts;
using Typrite.Contracts.Releases;
using Typrite.Domain.Devices;

namespace Typrite.Agent.Identity;

public sealed record EnrollResult(Guid DeviceId, Guid? AccountId);

public sealed record PendingCommand(Guid Id, string Type, string? PayloadJson, DateTimeOffset ExpiresAtUtc);

/// <summary>The agent's view of the device API. Token-bearing calls take the token explicitly so <see cref="TokenCache"/> stays in charge.</summary>
public sealed class DeviceApiClient(HttpClient http, TimeProvider time)
{
    public async Task<EnrollResult> EnrollAsync(string claimToken, string publicKeySpki, string appVersion, string cpuArchitecture, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("api/v1/devices/enroll",
            new { claimToken, publicKey = publicKeySpki, appVersion, cpuArchitecture }, ContractsJson.Options, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<EnrollResult>(ContractsJson.Options, ct).ConfigureAwait(false))!;
    }

    public async Task<IssuedToken> TokenAsync(Guid deviceId, System.Security.Cryptography.RSA key, CancellationToken ct)
    {
        var proof = ProofOfPossession.Create(deviceId, time.GetUtcNow(), key);
        using var res = await http.PostAsJsonAsync($"api/v1/devices/{deviceId}/tokens", proof, ContractsJson.Options, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IssuedToken>(ContractsJson.Options, ct).ConfigureAwait(false))!;
    }

    public async Task HeartbeatAsync(Guid deviceId, string token, object health, string appVersion, string cpuArchitecture, CancellationToken ct)
    {
        using var req = Authorized(HttpMethod.Post, $"api/v1/devices/{deviceId}/heartbeat", token);
        req.Content = JsonContent.Create(new { appVersion, cpuArchitecture, health }, options: ContractsJson.Options);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PendingCommand>> CommandsAsync(Guid deviceId, string token, CancellationToken ct)
    {
        using var req = Authorized(HttpMethod.Get, $"api/v1/devices/{deviceId}/commands", token);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<CommandList>(ContractsJson.Options, ct).ConfigureAwait(false);
        return body?.Items ?? [];
    }

    public async Task AckAsync(Guid deviceId, Guid commandId, string token, object result, CancellationToken ct)
    {
        using var req = Authorized(HttpMethod.Post, $"api/v1/devices/{deviceId}/commands/{commandId}/ack", token);
        req.Content = JsonContent.Create(new { result }, options: ContractsJson.Options);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task<CurrentReleaseResponse?> CurrentReleaseAsync(Guid deviceId, string token, CancellationToken ct)
    {
        using var req = Authorized(HttpMethod.Get, $"api/v1/devices/{deviceId}/releases/current", token);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CurrentReleaseResponse>(ContractsJson.Options, ct).ConfigureAwait(false);
    }

    public async Task UploadDiagnosticsAsync(Guid deviceId, string token, byte[] zip, CancellationToken ct)
    {
        using var req = Authorized(HttpMethod.Post, $"api/v1/devices/{deviceId}/diagnostics", token);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "diagnostics.zip");
        req.Content = form;
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> DownloadAsync(Uri url, long maxBytes, CancellationToken ct)
    {
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentLength is > 0 and var len && len > maxBytes)
        {
            throw new HttpRequestException($"Package is {len} bytes, above the {maxBytes} byte limit.");
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[256 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new HttpRequestException("Package exceeded the size limit while streaming.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token) =>
        new(method, url) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

    private sealed record CommandList(IReadOnlyList<PendingCommand> Items);
}
