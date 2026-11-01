using System.Net;
using Microsoft.Extensions.Time.Testing;
using Typrite.Desktop;

namespace Typrite.Desktop.Tests;

public sealed class DemoWeatherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "typrite-weather-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 18, 30, 0, TimeSpan.Zero));
    private static readonly WeatherCity Jaipur = new("Jaipur", "Rajasthan", 26.92, 75.82);
    private const string Forecast = """
        {"current":{"temperature_2m":21.3,"apparent_temperature":23.1,"relative_humidity_2m":71,
        "wind_speed_10m":0.6,"weather_code":0,"is_day":0,"time":1793557800},
        "daily":{"temperature_2m_max":[28.2],"temperature_2m_min":[18.9]},
        "hourly":{"time":[1793554200,1793561400],"temperature_2m":[22,20],"weather_code":[0,0],"is_day":[0,0]}}
        """;

    [Fact]
    public async Task Reads_real_forecast_fields_and_night_condition()
    {
        using var http = Client(Forecast);
        var result = await new DemoWeatherClient(http, _directory, _time).GetAsync(Jaipur, false, CancellationToken.None);
        Assert.False(result.IsStale);
        Assert.Equal(21.3, result.Weather!.TemperatureC);
        Assert.Equal("Clear night", result.Weather.Condition);
        Assert.Equal("🌙", result.Weather.Icon);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793557800), result.Weather.ObservedAtUtc);
        Assert.Equal(28.2, result.Weather.HighC);
        Assert.Single(result.Weather.Hours);
        Assert.Equal(20, result.Weather.Hours[0].TemperatureC);
    }

    [Fact]
    public async Task Offline_restart_recovers_last_known_city_and_marks_weather_stale()
    {
        using var http = Client(Forecast);
        await new DemoWeatherClient(http, _directory, _time).GetAsync(Jaipur, false, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(15));
        using var offline = Client("", HttpStatusCode.ServiceUnavailable);
        var restarted = new DemoWeatherClient(offline, _directory, _time);
        Assert.Equal(Jaipur, await restarted.DetectCityAsync(CancellationToken.None));
        var result = await restarted.GetAsync(Jaipur, false, CancellationToken.None);
        Assert.True(result.IsStale);
        Assert.Equal(21.3, result.Weather!.TemperatureC);
    }

    [Fact]
    public async Task Old_weather_is_not_presented_as_current()
    {
        using var http = Client(Forecast);
        await new DemoWeatherClient(http, _directory, _time).GetAsync(Jaipur, false, CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(25));
        using var offline = Client("", HttpStatusCode.ServiceUnavailable);
        var result = await new DemoWeatherClient(offline, _directory, _time).GetAsync(Jaipur, false, CancellationToken.None);
        Assert.Null(result.Weather);
    }

    [Fact]
    public async Task Changing_city_does_not_display_cached_weather_for_previous_city()
    {
        using var http = Client(Forecast);
        await new DemoWeatherClient(http, _directory, _time).GetAsync(Jaipur, false, CancellationToken.None);
        using var offline = Client("", HttpStatusCode.ServiceUnavailable);
        var result = await new DemoWeatherClient(offline, _directory, _time)
            .GetAsync(new("Delhi", "Delhi", 28.61, 77.21), false, CancellationToken.None);
        Assert.Null(result.Weather);
    }

    [Fact]
    public async Task Malformed_provider_response_becomes_retryable_state()
    {
        using var http = Client("{\"current\":null}");
        var result = await new DemoWeatherClient(http, _directory, _time).GetAsync(Jaipur, true, CancellationToken.None);
        Assert.Null(result.Weather);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Automatic_network_location_is_coarsened_to_city_precision()
    {
        using var http = Client("""{"success":true,"city":"Jodhpur","region":"Rajasthan","latitude":26.2684122,"longitude":73.0059396}""");
        var city = await new DemoWeatherClient(http, _directory, _time).DetectCityAsync(CancellationToken.None);
        Assert.Equal(new WeatherCity("Jodhpur", "Rajasthan", 26.27, 73.01), city);
    }

    [Fact]
    public async Task Device_coordinates_get_their_own_city_label()
    {
        using var http = Client("""{"city":"Jaipur","locality":"Jaipur","principalSubdivision":"Rajasthan"}""");
        var city = await new DemoWeatherClient(http, _directory, _time).CityFromDeviceAsync(26.92345, 75.82345, CancellationToken.None);
        Assert.Equal(Jaipur, city);
    }

    [Fact]
    public void Automatic_mode_clears_only_the_manual_city_override()
    {
        using var http = Client(Forecast);
        var client = new DemoWeatherClient(http, _directory, _time);
        Assert.Null(client.LoadCity());
        client.SaveCity(Jaipur);
        Assert.Equal(Jaipur, client.LoadCity());
        client.UseAutomaticCity();
        Assert.Null(client.LoadCity());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static HttpClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) => new(new ResponseHandler(body, status));

    private sealed class ResponseHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
