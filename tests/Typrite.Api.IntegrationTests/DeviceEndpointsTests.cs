using System.Net;
using System.Net.Http.Json;
using Typrite.Contracts;
using Typrite.Contracts.Configuration;

namespace Typrite.Api.IntegrationTests;

public sealed class DeviceEndpointsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Configuration_requires_device_auth()
    {
        var (deviceId, _) = await api.RegisterDeviceAsync();

        var res = await api.CreateClient().GetAsync($"api/v1/devices/{deviceId}/configuration");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Configuration_is_returned_with_etag_and_304_on_match()
    {
        var (deviceId, client) = await api.RegisterDeviceAsync();

        var first = await client.GetAsync($"api/v1/devices/{deviceId}/configuration");
        first.EnsureSuccessStatusCode();
        var config = await first.Content.ReadFromJsonAsync<DeviceConfiguration>(ContractsJson.Options);
        var etag = first.Headers.ETag;

        Assert.NotNull(config);
        Assert.Equal(1, config!.SchemaVersion);
        Assert.NotNull(etag);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/v1/devices/{deviceId}/configuration");
        req.Headers.IfNoneMatch.Add(etag!);
        var second = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Device_cannot_read_another_devices_configuration()
    {
        var (_, client) = await api.RegisterDeviceAsync();
        var (other, _) = await api.RegisterDeviceAsync();

        var res = await client.GetAsync($"api/v1/devices/{other}/configuration");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_updates_last_seen_and_version()
    {
        var (deviceId, client) = await api.RegisterDeviceAsync();
        var admin = await api.OperatorAsync("DeviceOperator");

        var res = await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/heartbeat",
            new { appVersion = "0.2.0", cpuArchitecture = "x64", health = new { cpuPercent = 3 } }, ContractsJson.Options);
        res.EnsureSuccessStatusCode();

        var list = await admin.GetFromJsonAsync<DeviceListResponse>("api/v1/admin/devices?pageSize=200", ContractsJson.Options);
        var me = Assert.Single(list!.Items, d => d.Id == deviceId);
        Assert.Equal("0.2.0", me.AppVersion);
        Assert.NotNull(me.LastSeenUtc);
    }

    [Fact]
    public async Task Dev_register_is_idempotent_for_the_same_secret_and_rejects_a_different_one()
    {
        var deviceId = Guid.NewGuid();
        var anon = api.CreateClient();

        var a = await anon.PostAsJsonAsync("api/v1/devices/dev-register", new { deviceId, secret = "s1", appVersion = "0" }, ContractsJson.Options);
        var b = await anon.PostAsJsonAsync("api/v1/devices/dev-register", new { deviceId, secret = "s1", appVersion = "0" }, ContractsJson.Options);
        var c = await anon.PostAsJsonAsync("api/v1/devices/dev-register", new { deviceId, secret = "s2", appVersion = "0" }, ContractsJson.Options);

        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, c.StatusCode);
    }

    public sealed record DeviceListResponse(IReadOnlyList<DeviceRow> Items, string? NextCursor);

    public sealed record DeviceRow(Guid Id, string Status, string? AppVersion, DateTimeOffset? LastSeenUtc);
}
