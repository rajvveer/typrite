using System.Net;
using System.Net.Http.Json;
using Typrite.Contracts;
using Typrite.Contracts.Weather;

namespace Typrite.Api.IntegrationTests;

public sealed class CommandAndWeatherTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Command_round_trip_issue_poll_ack()
    {
        var account = await api.CreateAccountAsync("cmd");
        var (deviceId, device) = await api.RegisterDeviceAsync(account);
        var op = await api.OperatorAsync("DeviceOperator", account);

        var issued = await op.PostAsJsonAsync($"api/v1/admin/devices/{deviceId}/commands", new { type = "refresh-configuration" }, ContractsJson.Options);
        issued.EnsureSuccessStatusCode();
        var commandId = (await issued.Content.ReadFromJsonAsync<ApiFixture.IdResponse>(ContractsJson.Options))!.Id;

        var pending = await device.GetFromJsonAsync<CommandList>($"api/v1/devices/{deviceId}/commands", ContractsJson.Options);
        Assert.Equal(commandId, Assert.Single(pending!.Items).Id);

        var ack = await device.PostAsJsonAsync($"api/v1/devices/{deviceId}/commands/{commandId}/ack", new { result = "ok" }, ContractsJson.Options);
        ack.EnsureSuccessStatusCode();

        var after = await device.GetFromJsonAsync<CommandList>($"api/v1/devices/{deviceId}/commands", ContractsJson.Options);
        Assert.Empty(after!.Items);
    }

    [Fact]
    public async Task Unknown_command_types_are_rejected()
    {
        var (deviceId, _) = await api.RegisterDeviceAsync();
        var op = await api.OperatorAsync("DeviceOperator");

        var res = await op.PostAsJsonAsync($"api/v1/admin/devices/{deviceId}/commands", new { type = "format-disk" }, ContractsJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Weather_is_served_to_devices_with_coarse_coordinates()
    {
        var (_, device) = await api.RegisterDeviceAsync();

        var report = await device.GetFromJsonAsync<WeatherReport>("api/v1/weather?lat=37.77&lon=-122.42", ContractsJson.Options);

        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report!.Condition));
        Assert.False(string.IsNullOrWhiteSpace(report.LocationLabel));
    }

    [Fact]
    public async Task Weather_without_location_returns_the_fallback_location()
    {
        var (_, device) = await api.RegisterDeviceAsync();

        var res = await device.GetAsync("api/v1/weather");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Diagnostics_zip_is_accepted_from_the_device_and_rejected_when_oversized()
    {
        var (deviceId, device) = await api.RegisterDeviceAsync();
        byte[] zip = [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "d.zip");
        var ok = await device.PostAsync($"api/v1/devices/{deviceId}/diagnostics", form);

        using var big = new MultipartFormDataContent();
        var bigFile = new ByteArrayContent(new byte[21 * 1024 * 1024]);
        bigFile.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        big.Add(bigFile, "file", "d.zip");
        var tooBig = await device.PostAsync($"api/v1/devices/{deviceId}/diagnostics", big);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
    }

    public sealed record CommandList(IReadOnlyList<CommandRow> Items);

    public sealed record CommandRow(Guid Id, string Type, string? PayloadJson, DateTimeOffset ExpiresAtUtc);
}
