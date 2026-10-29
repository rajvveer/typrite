using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Typrite.Contracts.Weather;

namespace Typrite.Infrastructure.Weather;

public interface IWeatherProvider
{
    Task<WeatherReport> GetAsync(double latitude, double longitude, CancellationToken ct);
}

public sealed record WeatherOptions
{
    public string Provider { get; init; } = "open-meteo";

    /// <summary>Used when the device has no location. Defaults to New York.</summary>
    public double FallbackLatitude { get; init; } = 40.71;

    public double FallbackLongitude { get; init; } = -74.01;

    public string FallbackLabel { get; init; } = "New York, NY";

    public int CacheMinutes { get; init; } = 15;
}

/// <summary>Coarsens coordinates to a 0.1° cell and caches per cell, so 10,000 devices in one city share a handful of upstream calls.</summary>
public sealed class WeatherService(IWeatherProvider provider, WeatherOptions options, TimeProvider time)
{
    private readonly ConcurrentDictionary<(double, double), (WeatherReport Report, DateTimeOffset FetchedAt)> _cache = new();

    public async Task<WeatherReport> GetAsync(double? latitude, double? longitude, CancellationToken ct)
    {
        var fallback = latitude is null || longitude is null;
        var lat = Math.Round(latitude ?? options.FallbackLatitude, 1);
        var lon = Math.Round(longitude ?? options.FallbackLongitude, 1);
        var now = time.GetUtcNow();

        if (_cache.TryGetValue((lat, lon), out var hit) && now - hit.FetchedAt < TimeSpan.FromMinutes(options.CacheMinutes))
        {
            return Label(hit.Report, fallback);
        }

        var report = await provider.GetAsync(lat, lon, ct).ConfigureAwait(false);
        _cache[(lat, lon)] = (report, now);
        return Label(report, fallback);
    }

    private WeatherReport Label(WeatherReport r, bool fallback) => fallback ? r with { LocationLabel = options.FallbackLabel } : r;
}

/// <summary>Deterministic weather for tests and offline development.</summary>
public sealed class MockWeatherProvider(TimeProvider time) : IWeatherProvider
{
    private static readonly string[] Conditions = ["Clear", "Partly cloudy", "Overcast", "Light rain", "Windy"];

    public Task<WeatherReport> GetAsync(double latitude, double longitude, CancellationToken ct)
    {
        var seed = (int)Math.Abs(latitude * 10 + longitude * 7);
        var report = new WeatherReport(
            TemperatureC: 10 + (seed % 15),
            Condition: Conditions[seed % Conditions.Length],
            LocationLabel: string.Create(CultureInfo.InvariantCulture, $"{latitude:0.0}, {longitude:0.0}"),
            ObservedAtUtc: time.GetUtcNow());
        return Task.FromResult(report);
    }
}

/// <summary>Open-Meteo needs no API key; the backend still proxies it so devices never talk to third parties directly.</summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http, TimeProvider time) : IWeatherProvider
{
    public async Task<WeatherReport> GetAsync(double latitude, double longitude, CancellationToken ct)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&timezone=UTC");
        using var doc = await http.GetFromJsonAsync<JsonDocument>(url, ct).ConfigureAwait(false)
            ?? throw new HttpRequestException("Empty weather response.");
        var current = doc.RootElement.GetProperty("current");
        return new WeatherReport(
            TemperatureC: current.GetProperty("temperature_2m").GetDouble(),
            Condition: Describe(current.GetProperty("weather_code").GetInt32()),
            LocationLabel: string.Create(CultureInfo.InvariantCulture, $"{latitude:0.0}, {longitude:0.0}"),
            ObservedAtUtc: time.GetUtcNow());
    }

    /// <summary>WMO weather interpretation codes.</summary>
    internal static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 or 2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        85 or 86 => "Snow showers",
        >= 95 => "Thunderstorm",
        _ => "Unknown",
    };
}
