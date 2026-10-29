using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Typrite.Contracts;
using Typrite.Contracts.Delivery;

namespace Typrite.Api.IntegrationTests;

public sealed class DeliveryEndpointsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static AdDecisionRequest Request(string? key = null) =>
        new("bar", key ?? Guid.NewGuid().ToString("N"), 1, ["400x225", "384x216", "300x250"]);

    private async Task<Guid> ActiveCampaignAsync(Guid accountId, string format = "400x225", byte[]? bytes = null)
    {
        var op = await api.OperatorAsync("CampaignOperator", accountId);
        var created = await op.PostAsJsonAsync("api/v1/admin/campaigns",
            new { name = "Autumn", startsAtUtc = DateTimeOffset.UtcNow.AddDays(-1), endsAtUtc = DateTimeOffset.UtcNow.AddDays(7), weight = 1 }, ContractsJson.Options);
        created.EnsureSuccessStatusCode();
        var campaignId = (await created.Content.ReadFromJsonAsync<ApiFixture.IdResponse>(ContractsJson.Options))!.Id;

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "c.png");
        form.Add(new StringContent(format), "format");
        var upload = await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/creatives", form);
        upload.EnsureSuccessStatusCode();

        (await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/approve", null)).EnsureSuccessStatusCode();
        (await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/activate", null)).EnsureSuccessStatusCode();
        return campaignId;
    }

    [Fact]
    public async Task No_eligible_campaign_is_a_204_no_fill()
    {
        var account = await api.CreateAccountAsync("empty");
        var (deviceId, client) = await api.RegisterDeviceAsync(account);

        var res = await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/disp-1/decisions", Request(), ContractsJson.Options);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Active_campaign_yields_a_decision_with_a_verifiable_creative()
    {
        var account = await api.CreateAccountAsync("filled");
        var campaignId = await ActiveCampaignAsync(account);
        var (deviceId, client) = await api.RegisterDeviceAsync(account);

        var res = await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/disp-1/decisions", Request(), ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        var decision = await res.Content.ReadFromJsonAsync<AdDecisionResponse>(ContractsJson.Options);

        Assert.NotNull(decision);
        Assert.Equal(campaignId, decision!.CampaignId);
        Assert.Equal("400x225", decision.Creative.Format);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Png)), decision.Creative.Sha256);
        Assert.Equal(Uri.UriSchemeHttps, decision.Creative.ContentUri.Scheme);

        var content = await client.GetAsync(decision.Creative.ContentUri.PathAndQuery);
        content.EnsureSuccessStatusCode();
        Assert.Equal(Png, await content.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", content.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_decision()
    {
        var account = await api.CreateAccountAsync("idem");
        await ActiveCampaignAsync(account);
        var (deviceId, client) = await api.RegisterDeviceAsync(account);
        var request = Request("key-1");

        var a = await (await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/d/decisions", request, ContractsJson.Options)).Content.ReadFromJsonAsync<AdDecisionResponse>(ContractsJson.Options);
        var b = await (await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/d/decisions", request, ContractsJson.Options)).Content.ReadFromJsonAsync<AdDecisionResponse>(ContractsJson.Options);

        Assert.Equal(a!.DecisionId, b!.DecisionId);
    }

    [Fact]
    public async Task Devices_only_see_campaigns_of_their_own_account()
    {
        var a = await api.CreateAccountAsync("a");
        var b = await api.CreateAccountAsync("b");
        await ActiveCampaignAsync(a);
        var (deviceId, client) = await api.RegisterDeviceAsync(b);

        var res = await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/d/decisions", Request(), ContractsJson.Options);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Creative_upload_rejects_bytes_that_do_not_match_the_media_type()
    {
        var account = await api.CreateAccountAsync("badbytes");
        var op = await api.OperatorAsync("CampaignOperator", account);
        var created = await op.PostAsJsonAsync("api/v1/admin/campaigns",
            new { name = "x", startsAtUtc = DateTimeOffset.UtcNow, endsAtUtc = DateTimeOffset.UtcNow.AddDays(1), weight = 1 }, ContractsJson.Options);
        var campaignId = (await created.Content.ReadFromJsonAsync<ApiFixture.IdResponse>(ContractsJson.Options))!.Id;

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "c.png");
        form.Add(new StringContent("400x225"), "format");
        var upload = await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/creatives", form);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task Paused_campaign_stops_serving_and_is_audited()
    {
        var account = await api.CreateAccountAsync("pause");
        var campaignId = await ActiveCampaignAsync(account);
        var (deviceId, client) = await api.RegisterDeviceAsync(account);
        var op = await api.OperatorAsync("CampaignOperator", account, subject: "pauser");

        (await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/pause", null)).EnsureSuccessStatusCode();
        var res = await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/d/decisions", Request(), ContractsJson.Options);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var admin = await api.OperatorAsync("Administrator", account);
        var audit = await admin.GetFromJsonAsync<AuditResponse>($"api/v1/admin/audit?resource=campaign:{campaignId}", ContractsJson.Options);
        Assert.Contains(audit!.Items, a => a.Action == "campaign.pause" && a.Actor == "pauser");
    }

    public sealed record AuditResponse(IReadOnlyList<AuditRow> Items);

    public sealed record AuditRow(string Actor, string Action, string Resource);
}
