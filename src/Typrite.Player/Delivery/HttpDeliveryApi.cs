using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Typrite.Contracts;
using Typrite.Contracts.Delivery;

namespace Typrite.Player.Delivery;

/// <summary>HTTP implementation. The token provider returns the current short-lived device token (section 3 of the guide).</summary>
public sealed class HttpDeliveryApi : IDeliveryApi
{
    private readonly HttpClient _http;
    private readonly Guid _deviceId;
    private readonly Func<CancellationToken, ValueTask<string>> _token;

    public HttpDeliveryApi(HttpClient http, Guid deviceId, Func<CancellationToken, ValueTask<string>> tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _http = http;
        _deviceId = deviceId;
        _token = tokenProvider;
    }

    public async Task<AdDecisionResponse?> RequestDecisionAsync(string displayKey, AdDecisionRequest request, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/v1/devices/{_deviceId}/displays/{Uri.EscapeDataString(displayKey)}/decisions")
        {
            Content = JsonContent.Create(request, options: ContractsJson.Options),
        };
        await AuthorizeAsync(req, ct).ConfigureAwait(false);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<AdDecisionResponse>(ContractsJson.Options, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> FetchCreativeAsync(Uri contentUri, long maxBytes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, contentUri);
        if (_http.BaseAddress is { } origin && contentUri.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
        {
            await AuthorizeAsync(req, ct).ConfigureAwait(false);
        }

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentLength is > 0 and var declared && declared > maxBytes)
        {
            throw new HttpRequestException($"Creative is {declared} bytes, above the {maxBytes} byte limit.");
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new HttpRequestException($"Creative exceeded the {maxBytes} byte limit while streaming.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public async Task<EventBatchResponse> PostEventsAsync(EventBatchRequest batch, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/events/batches")
        {
            Content = JsonContent.Create(batch, options: ContractsJson.Options),
        };
        await AuthorizeAsync(req, ct).ConfigureAwait(false);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options, ct).ConfigureAwait(false))!;
    }

    private async Task AuthorizeAsync(HttpRequestMessage req, CancellationToken ct) =>
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token(ct).ConfigureAwait(false));
}
