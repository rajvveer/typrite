using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Typrite.Api.Auth;
using Typrite.Domain.Devices;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api.Endpoints;

public static class DeviceEndpoints
{
    public sealed record HeartbeatRequest(string? AppVersion, string? CpuArchitecture, JsonElement? Health);

    public sealed record AckRequest(JsonElement? Result);

    public const long MaxDiagnosticsBytes = 20 * 1024 * 1024;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/devices/{deviceId:guid}")
            .RequireAuthorization(Policies.Device)
            .RequireRateLimiting("device")
            .WithTags("Device");

        g.MapGet("/configuration", async (Guid deviceId, HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var me = DevicePrincipal.From(http.User);
            if (me.DeviceId != deviceId)
            {
                return Results.Forbid();
            }

            var config = await db.Configurations.AsNoTracking()
                .Where(c => c.AccountId == me.AccountId || c.AccountId == null)
                .OrderByDescending(c => c.AccountId != null).ThenByDescending(c => c.Version)
                .FirstOrDefaultAsync(ct);
            if (config is null)
            {
                return Results.NotFound();
            }

            var etag = $"\"{config.Sha256}\"";
            if (http.Request.Headers.IfNoneMatch.Any(v => v == etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            http.Response.Headers[HeaderNames.ETag] = etag;
            http.Response.Headers[HeaderNames.CacheControl] = "private, max-age=0";
            return Results.Content(config.Json, "application/json");
        });

        g.MapPost("/heartbeat", async (Guid deviceId, HeartbeatRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = DevicePrincipal.From(http.User);
            if (me.DeviceId != deviceId)
            {
                return Results.Forbid();
            }

            var device = await db.Devices.FirstAsync(d => d.Id == deviceId, ct);
            device.LastSeenUtc = time.GetUtcNow();
            device.AppVersion = req.AppVersion ?? device.AppVersion;
            device.CpuArchitecture = req.CpuArchitecture ?? device.CpuArchitecture;
            device.LastHealthJson = req.Health?.GetRawText();
            await db.SaveChangesAsync(ct);

            var latestConfig = await db.Configurations.AsNoTracking()
                .Where(c => c.AccountId == me.AccountId || c.AccountId == null)
                .OrderByDescending(c => c.AccountId != null).ThenByDescending(c => c.Version)
                .Select(c => c.Version).FirstOrDefaultAsync(ct);
            return Results.Ok(new { serverTimeUtc = time.GetUtcNow(), configurationVersion = latestConfig });
        });

        g.MapGet("/commands", async (Guid deviceId, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (DevicePrincipal.From(http.User).DeviceId != deviceId)
            {
                return Results.Forbid();
            }

            var now = time.GetUtcNow();
            var pending = await db.RemoteCommands.Where(c => c.DeviceId == deviceId && c.Status == CommandStatus.Pending).ToListAsync(ct);
            foreach (var expired in pending.Where(c => c.ExpiresAtUtc <= now))
            {
                expired.Status = CommandStatus.Expired;
            }

            await db.SaveChangesAsync(ct);
            var items = pending.Where(c => c.Status == CommandStatus.Pending)
                .Select(c => new { c.Id, c.Type, c.PayloadJson, c.ExpiresAtUtc });
            return Results.Ok(new { items });
        });

        g.MapPost("/diagnostics", async (Guid deviceId, IFormFile file, HttpContext http, Typrite.Infrastructure.Storage.IBlobStorage blobs, TimeProvider time, CancellationToken ct) =>
        {
            if (DevicePrincipal.From(http.User).DeviceId != deviceId)
            {
                return Results.Forbid();
            }

            if (file.Length is <= 0 or > MaxDiagnosticsBytes)
            {
                return Results.BadRequest(new { error = $"diagnostics must be 1..{MaxDiagnosticsBytes} bytes" });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var key = $"diagnostics/{deviceId:N}/{time.GetUtcNow():yyyyMMddHHmmss}.zip";
            await blobs.PutAsync(key, ms.ToArray(), "application/zip", ct);
            return Results.Ok(new { key, bytes = ms.Length });
        }).DisableAntiforgery();

        g.MapPost("/commands/{commandId:guid}/ack", async (Guid deviceId, Guid commandId, AckRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (DevicePrincipal.From(http.User).DeviceId != deviceId)
            {
                return Results.Forbid();
            }

            var command = await db.RemoteCommands.FirstOrDefaultAsync(c => c.Id == commandId && c.DeviceId == deviceId, ct);
            if (command is null)
            {
                return Results.NotFound();
            }

            if (command.Status == CommandStatus.Pending)
            {
                command.Status = CommandStatus.Acknowledged;
                command.AcknowledgedAtUtc = time.GetUtcNow();
                command.ResultJson = req.Result?.GetRawText();
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { command.Id, status = command.Status.ToString() });
        });
    }
}
