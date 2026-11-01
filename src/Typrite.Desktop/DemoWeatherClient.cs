using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Typrite.Desktop;

public sealed record WeatherCity(string Name, string Region, double Latitude, double Longitude)
{
    public string Label => string.IsNullOrWhiteSpace(Region) ? Name : $"{Name}, {Region}";
}

public sealed record DemoWeather(
    WeatherCity City, double TemperatureC, double FeelsLikeC, double Humidity,
    double WindKmh, double HighC, double LowC, int Code, bool IsDay,
    DateTimeOffset ObservedAtUtc, DateTimeOffset FetchedAtUtc)
{
    public IReadOnlyList<WeatherHour> Hours { get; init; } = [];
    public string Condition => Code switch
    {
        0 => IsDay ? "Clear skies" : "Clear night",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Rain showers",
        85 or 86 => "Snow showers",
        >= 95 and <= 99 => "Thunderstorms",
        _ => "Current weather",
    };

    public string Icon => IconFor(Code, IsDay);

    public static string IconFor(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "☀️" : "🌙",
        2 => isDay ? "⛅" : "☁️",
        45 or 48 => "🌫️",
        >= 51 and <= 67 or >= 80 and <= 82 => "🌧️",
        >= 71 and <= 77 or 85 or 86 => "❄️",
        >= 95 and <= 99 => "⛈️",
        _ => "☁️",
    };
}

public sealed record WeatherHour(DateTimeOffset Time, double TemperatureC, int Code, bool IsDay)
{
    public string Label => Time.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    public string Temperature => string.Create(CultureInfo.CurrentCulture, $"{TemperatureC:0}°");
    public string Icon => DemoWeather.IconFor(Code, IsDay);
    public string Symbol => Code switch
    {
        0 or 1 => IsDay ? "☀" : "☾",
        >= 51 and <= 67 or >= 80 and <= 82 => "☂",
        >= 71 and <= 77 or 85 or 86 => "❄",
        >= 95 and <= 99 => "ϟ",
        _ => "☁",
    };
}

public sealed record WeatherFetch(DemoWeather? Weather, bool IsStale, string? Error);

/// <summary>Standalone demo weather. Production devices continue using the authenticated weather proxy.</summary>
public sealed class DemoWeatherClient(HttpClient http, string dataDirectory, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DemoWeather? _cached;

    public WeatherCity? LoadCity()
    {
        try
        {
            var path = Path.Combine(dataDirectory, "weather-city.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<WeatherCity>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void SaveCity(WeatherCity city)
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "weather-city.json"), JsonSerializer.Serialize(city, JsonOptions));
    }

    public void UseAutomaticCity()
    {
        var path = Path.Combine(dataDirectory, "weather-city.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public async Task<WeatherCity?> DetectCityAsync(CancellationToken ct)
    {
        try
        {
            using var doc = await http.GetFromJsonAsync<JsonDocument>(
                "https://ipwho.is/?fields=success,city,region,latitude,longitude", ct).ConfigureAwait(false);
            if (doc is null || !doc.RootElement.GetProperty("success").GetBoolean()) return LastKnownCity();
            var result = doc.RootElement;
            return new WeatherCity(result.GetProperty("city").GetString() ?? "Current location",
                result.GetProperty("region").GetString() ?? "",
                Math.Round(result.GetProperty("latitude").GetDouble(), 2),
                Math.Round(result.GetProperty("longitude").GetDouble(), 2));
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return LastKnownCity();
        }
    }

    public async Task<WeatherCity> CityFromDeviceAsync(double latitude, double longitude, CancellationToken ct)
    {
        latitude = Math.Round(latitude, 2);
        longitude = Math.Round(longitude, 2);
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={latitude:0.00}&longitude={longitude:0.00}&localityLanguage=en");
            using var doc = await http.GetFromJsonAsync<JsonDocument>(url, ct).ConfigureAwait(false);
            if (doc is not null)
            {
                var root = doc.RootElement;
                var name = root.GetProperty("city").GetString();
                if (string.IsNullOrWhiteSpace(name)) name = root.GetProperty("locality").GetString();
                return new WeatherCity(name ?? "Current location", root.GetProperty("principalSubdivision").GetString() ?? "", latitude, longitude);
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException
            || error is OperationCanceledException && !ct.IsCancellationRequested) { }
        return new WeatherCity("Current location", "", latitude, longitude);
    }

    private WeatherCity? LastKnownCity()
    {
        try
        {
            var path = Path.Combine(dataDirectory, "weather-cache.json");
            if (File.Exists(path)) _cached = JsonSerializer.Deserialize<DemoWeather>(File.ReadAllText(path), JsonOptions);
            return _cached is not null && time.GetUtcNow() - _cached.FetchedAtUtc < TimeSpan.FromHours(24) ? _cached.City : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public async Task<IReadOnlyList<WeatherCity>> SearchAsync(string name, CancellationToken ct)
    {
        if (name.Trim().Length < 2) return [];
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name.Trim())}&count=5&language=en&format=json";
        using var doc = await http.GetFromJsonAsync<JsonDocument>(url, ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results)) return [];
        return results.EnumerateArray().Select(item => new WeatherCity(
            item.GetProperty("name").GetString()!,
            item.TryGetProperty("admin1", out var region) ? region.GetString() ?? "" :
                item.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "",
            item.GetProperty("latitude").GetDouble(), item.GetProperty("longitude").GetDouble())).ToArray();
    }

    public async Task<WeatherFetch> GetAsync(WeatherCity city, bool force, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var cached = ReadCache(city);
        if (!force && cached is not null && now - cached.FetchedAtUtc < TimeSpan.FromMinutes(10))
            return new(cached, false, null);

        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={city.Latitude:0.00}&longitude={city.Longitude:0.00}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&hourly=temperature_2m,weather_code,is_day&forecast_hours=8&timezone=auto&timeformat=unixtime&forecast_days=1");
            using var doc = await http.GetFromJsonAsync<JsonDocument>(url, ct).ConfigureAwait(false)
                ?? throw new JsonException("Empty weather response.");
            var current = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.GetProperty("daily");
            var weather = new DemoWeather(city,
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("apparent_temperature").GetDouble(),
                current.GetProperty("relative_humidity_2m").GetDouble(),
                current.GetProperty("wind_speed_10m").GetDouble(),
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                current.GetProperty("weather_code").GetInt32(), current.GetProperty("is_day").GetInt32() == 1,
                DateTimeOffset.FromUnixTimeSeconds(current.GetProperty("time").GetInt64()), now);
            if (doc.RootElement.TryGetProperty("hourly", out var hourly))
            {
                var hours = new List<WeatherHour>();
                var timestamps = hourly.GetProperty("time");
                for (var i = 0; i < timestamps.GetArrayLength() && hours.Count < 6; i++)
                {
                    var instant = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64());
                    if (instant <= now) continue;
                    hours.Add(new WeatherHour(instant, hourly.GetProperty("temperature_2m")[i].GetDouble(),
                        hourly.GetProperty("weather_code")[i].GetInt32(), hourly.GetProperty("is_day")[i].GetInt32() == 1));
                }
                weather = weather with { Hours = hours };
            }
            _cached = weather;
            try
            {
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(Path.Combine(dataDirectory, "weather-cache.json"), JsonSerializer.Serialize(weather, JsonOptions));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return new(weather, false, null);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return cached is not null && now - cached.FetchedAtUtc < TimeSpan.FromHours(24)
                ? new(cached, true, "Offline · last update")
                : new(null, false, "Couldn't update · tap refresh");
        }
    }

    private DemoWeather? ReadCache(WeatherCity city)
    {
        if (_cached is null)
        {
            try
            {
                var path = Path.Combine(dataDirectory, "weather-cache.json");
                if (File.Exists(path)) _cached = JsonSerializer.Deserialize<DemoWeather>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return _cached?.City == city ? _cached : null;
    }
}
