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

        // Decisions referenced must have been issued to this device; counts per (decision, type) are capped so a
        // local user holding a device token cannot fabricate impressions.
        var decisionIds = candidates.Where(e => e.DecisionId is not null).Select(e => e.DecisionId!.Value).Distinct().ToList();
        var ownedDecisions = await db.AdDecisions.AsNoTracking()
            .Where(d => d.DeviceId == me.DeviceId && decisionIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToHashSetAsync(ct);
        var perDecisionCounts = await db.DeliveryEvents.AsNoTracking()
            .Where(r => r.DeviceId == me.DeviceId && r.DecisionId != null && decisionIds.Contains(r.DecisionId.Value))
            .GroupBy(r => new { r.DecisionId, r.Type })
            .Select(g => new { g.Key.DecisionId, g.Key.Type, Count = g.Count() })
            .ToDictionaryAsync(g => (g.DecisionId!.Value, g.Type), g => g.Count, ct);

        foreach (var e in candidates)
        {
            if (e.DecisionId is { } decisionId)
            {
                if (!ownedDecisions.Contains(decisionId))
                {
                    rejected.Add(e.ClientEventId);
                    continue;
                }

                if (!known.Contains(e.ClientEventId))
                {
                    var key = (decisionId, e.Type);
                    var seen = perDecisionCounts.GetValueOrDefault(key);
                    if (seen >= EventIngestRules.MaxPerDecision(e.Type))
                    {
                        rejected.Add(e.ClientEventId);
                        continue;
                    }

                    perDecisionCounts[key] = seen + 1;
                }
            }

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
