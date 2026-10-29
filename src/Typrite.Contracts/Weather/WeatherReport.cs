namespace Typrite.Contracts.Weather;

public sealed record WeatherReport(double TemperatureC, string Condition, string LocationLabel, DateTimeOffset ObservedAtUtc);
