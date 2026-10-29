using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Typrite.Api.Auth;
using Typrite.Contracts;
using Typrite.Contracts.Delivery;
using Typrite.Domain.Audit;
using Typrite.Domain.Delivery;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api.Endpoints;

public static class EventEndpoints
{
    public const int MaxBatch = 1000;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/events/batches", IngestAsync)
            .RequireAuthorization(Policies.Device)
            .RequireRateLimiting("device")
            .WithTags("Events");
    }

    /// <summary>
    /// Validates each event, stores new ones, and acknowledges both new and already-stored ids so the device can
    /// delete them. Rejections are permanent and reported so the device drops them instead of retrying forever.
    /// </summary>
    private static async Task<IResult> IngestAsync(EventBatchRequest batch, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (batch.Events is null || batch.Events.Count == 0 || batch.Events.Count > MaxBatch)
        {
            return Results.BadRequest(new { error = $"batch must contain 1..{MaxBatch} events" });
        }

        var me = DevicePrincipal.From(http.User);
        var now = time.GetUtcNow();
        var accepted = new List<Guid>();
        var rejected = new List<Guid>();
        var candidates = new List<DeliveryEvent>();

        foreach (var e in batch.Events)
        {
            if (EventIngestRules.Reject(e, me.DeviceId, now) is null)
            {
                candidates.Add(e);
            }
            else
            {
                rejected.Add(e.ClientEventId);
            }
        }

        var ids = candidates.Select(e => e.ClientEventId).Distinct().ToList();
        var known = await db.DeliveryEvents.AsNoTracking()
            .Where(r => r.DeviceId == me.DeviceId && ids.Contains(r.ClientEventId))
            .Select(r => r.ClientEventId)
            .ToHashSetAsync(ct);

        foreach (var e in candidates)
        {
            if (known.Add(e.ClientEventId))
            {
                db.DeliveryEvents.Add(new DeliveryEventRecord
                {
                    ClientEventId = e.ClientEventId,
                    DeviceId = e.DeviceId,
                    SessionId = e.SessionId,
                    DisplayKey = e.DisplayKey,
                    DecisionId = e.DecisionId,
                    CreativeId = e.CreativeId,
                    Type = e.Type,
                    DeviceTimeUtc = e.DeviceTimeUtc,
                    ReceivedAtUtc = now,
                    ReportedDurationMs = e.ReportedDurationMs,
                    ConfigurationVersion = e.ConfigurationVersion,
                    SchemaVersion = e.SchemaVersion,
                });
            }

            accepted.Add(e.ClientEventId);
        }

        if (db.ChangeTracker.HasChanges())
        {
            db.Outbox.Add(new OutboxMessage
            {
                Type = "events-ingested",
                PayloadJson = JsonSerializer.Serialize(new { deviceId = me.DeviceId, count = accepted.Count, receivedAtUtc = now }, ContractsJson.Options),
                CreatedAtUtc = now,
            });
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new EventBatchResponse(accepted, rejected));
    }
}
