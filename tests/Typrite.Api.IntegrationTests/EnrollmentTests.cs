using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Typrite.Contracts;
using Typrite.Contracts.Releases;
using Typrite.Domain.Devices;

namespace Typrite.Api.IntegrationTests;

public sealed class EnrollmentTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record ClaimResponse(Guid Id, string Token, DateTimeOffset ExpiresAtUtc);

    private sealed record EnrollResponse(Guid DeviceId, Guid AccountId);

    private sealed record TokenResponse(string Token, DateTimeOffset ExpiresAtUtc);

    private async Task<string> ClaimAsync(Guid account)
    {
        var admin = await api.OperatorAsync("DeviceOperator", account);
        var res = await admin.PostAsJsonAsync("api/v1/admin/enrollment-claims", new { ttlMinutes = 60 }, ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ClaimResponse>(ContractsJson.Options))!.Token;
    }

    private async Task<(Guid DeviceId, RSA Key)> EnrollAsync(Guid account)
    {
        var key = RSA.Create(2048);
        var token = await ClaimAsync(account);
        var res = await api.CreateClient().PostAsJsonAsync("api/v1/devices/enroll",
            new { claimToken = token, publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), appVersion = "1.0.0", cpuArchitecture = "x64" },
            ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<EnrollResponse>(ContractsJson.Options);
        Assert.Equal(account, body!.AccountId);
        return (body.DeviceId, key);
    }

    private async Task<HttpClient> DeviceClientAsync(Guid deviceId, RSA key)
    {
        var proof = ProofOfPossession.Create(deviceId, DateTimeOffset.UtcNow, key);
        var res = await api.CreateClient().PostAsJsonAsync($"api/v1/devices/{deviceId}/tokens", proof, ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<TokenResponse>(ContractsJson.Options))!.Token;
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Claim_enrolls_once_and_the_key_then_obtains_tokens()
    {
        var account = await api.CreateAccountAsync("enroll");
        var (deviceId, key) = await EnrollAsync(account);
        using var _ = key;

        var client = await DeviceClientAsync(deviceId, key);
        var config = await client.GetAsync($"api/v1/devices/{deviceId}/configuration");

        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
    }

    [Fact]
    public async Task Used_or_bogus_claims_are_rejected()
    {
        var account = await api.CreateAccountAsync("claims");
        var token = await ClaimAsync(account);
        using var key = RSA.Create(2048);
        var body = new { claimToken = token, publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), appVersion = "1", cpuArchitecture = "x64" };

        var first = await api.CreateClient().PostAsJsonAsync("api/v1/devices/enroll", body, ContractsJson.Options);
        var second = await api.CreateClient().PostAsJsonAsync("api/v1/devices/enroll", body, ContractsJson.Options);
        var bogus = await api.CreateClient().PostAsJsonAsync("api/v1/devices/enroll", body with { claimToken = "nope" }, ContractsJson.Options);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task Token_requires_a_valid_proof_from_the_enrolled_key()
    {
        var account = await api.CreateAccountAsync("pop");
        var (deviceId, key) = await EnrollAsync(account);
        using var _ = key;
        using var wrong = RSA.Create(2048);

        var forged = await api.CreateClient().PostAsJsonAsync($"api/v1/devices/{deviceId}/tokens", ProofOfPossession.Create(deviceId, DateTimeOffset.UtcNow, wrong), ContractsJson.Options);
        var stale = await api.CreateClient().PostAsJsonAsync($"api/v1/devices/{deviceId}/tokens", ProofOfPossession.Create(deviceId, DateTimeOffset.UtcNow.AddHours(-1), key), ContractsJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    [Fact]
    public async Task Dev_secret_is_not_accepted_for_a_key_enrolled_device()
    {
        var account = await api.CreateAccountAsync("nodev");
        var (deviceId, key) = await EnrollAsync(account);
        using var _ = key;
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "some-secret");

        var res = await client.GetAsync($"api/v1/devices/{deviceId}/configuration");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Current_release_follows_cohort_and_architecture_and_is_signed()
    {
        var account = await api.CreateAccountAsync("release");
        var (deviceId, key) = await EnrollAsync(account);
        using var _ = key;
        var client = await DeviceClientAsync(deviceId, key);
        var mgr = await api.OperatorAsync("ReleaseManager");
        var version = $"3.0.{Random.Shared.Next(1, 1_000_000)}";
        var created = await mgr.PostAsJsonAsync("api/v1/admin/releases",
            new { version, cpuArchitecture = "x64", packageSha256 = new string('d', 64), packageUrl = "https://cdn.example.com/t.msi", manifestSignature = "" }, ContractsJson.Options);
        var releaseId = (await created.Content.ReadFromJsonAsync<ApiFixture.IdResponse>(ContractsJson.Options))!.Id;

        var none = await client.GetAsync($"api/v1/devices/{deviceId}/releases/current");
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);

        (await mgr.PostAsJsonAsync($"api/v1/admin/releases/{releaseId}/rollout", new { percent = 100 }, ContractsJson.Options)).EnsureSuccessStatusCode();
        var current = await client.GetFromJsonAsync<CurrentReleaseResponse>($"api/v1/devices/{deviceId}/releases/current", ContractsJson.Options);

        Assert.NotNull(current);
        Assert.Equal(version, current!.Manifest.Version);
        var publisherPem = await api.CreateClient().GetStringAsync("api/v1/releases/publisher-key");
        Assert.True(ReleaseManifestSigner.Verify(current.Manifest, current.SignatureBase64, publisherPem));
    }
}
