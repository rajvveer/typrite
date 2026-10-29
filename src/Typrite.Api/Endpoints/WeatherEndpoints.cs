using Typrite.Api.Auth;
using Typrite.Infrastructure.Weather;

namespace Typrite.Api.Endpoints;

public static class WeatherEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/weather", async (double? lat, double? lon, WeatherService weather, CancellationToken ct) =>
        {
            if (lat is < -90 or > 90 || lon is < -180 or > 180)
            {
                return Results.BadRequest(new { error = "coordinates out of range" });
            }

            return Results.Ok(await weather.GetAsync(lat, lon, ct));
        })
        .RequireAuthorization(Policies.Device)
        .RequireRateLimiting("device")
        .WithTags("Weather");
    }
}
