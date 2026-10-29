using System.Net;
using System.Net.Http.Json;
using Typrite.Contracts;

namespace Typrite.Api.IntegrationTests;

public sealed class AdminAuthorizationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static object NewCampaign(string name) =>
        new { name, startsAtUtc = DateTimeOffset.UtcNow, endsAtUtc = DateTimeOffset.UtcNow.AddDays(1), weight = 1 };

    [Fact]
    public async Task Viewer_cannot_create_campaigns()
    {
        var viewer = await api.OperatorAsync("Viewer");

        var res = await viewer.PostAsJsonAsync("api/v1/admin/campaigns", NewCampaign("v"), ContractsJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Device_token_cannot_call_admin_endpoints()
    {
        var (_, device) = await api.RegisterDeviceAsync();

        var res = await device.GetAsync("api/v1/admin/devices");

        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operators_only_see_their_own_accounts_campaigns()
    {
        var a = await api.CreateAccountAsync("tenant-a");
        var b = await api.CreateAccountAsync("tenant-b");
        var opA = await api.OperatorAsync("CampaignOperator", a);
        var opB = await api.OperatorAsync("CampaignOperator", b);
        var created = await opA.PostAsJsonAsync("api/v1/admin/campaigns", NewCampaign("secret-a"), ContractsJson.Options);
        var id = (await created.Content.ReadFromJsonAsync<ApiFixture.IdResponse>(ContractsJson.Options))!.Id;

        var listB = await opB.GetFromJsonAsync<CampaignList>("api/v1/admin/campaigns", ContractsJson.Options);
        var pauseB = await opB.PostAsync($"api/v1/admin/campaigns/{id}/pause", null);

        Assert.DoesNotContain(listB!.Items, c => c.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, pauseB.StatusCode);
    }

    [Fact]
    public async Task Only_release_managers_create_releases()
    {
        var campaignOp = await api.OperatorAsync("CampaignOperator");
        var releaseMgr = await api.OperatorAsync("ReleaseManager");
        var release = new { version = $"1.0.{Random.Shared.Next(1, 1_000_000)}", cpuArchitecture = "x64", packageSha256 = new string('a', 64), packageUrl = "https://cdn.example.com/typrite-1.0.0.msi", manifestSignature = "sig" };

        var denied = await campaignOp.PostAsJsonAsync("api/v1/admin/releases", release, ContractsJson.Options);
        var ok = await releaseMgr.PostAsJsonAsync("api/v1/admin/releases", release, ContractsJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    public sealed record CampaignList(IReadOnlyList<CampaignRow> Items);

    public sealed record CampaignRow(Guid Id, string Name, string Status);
}
