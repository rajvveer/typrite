using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Typrite.Domain.Delivery;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Worker.Jobs;

/// <summary>
/// Consumes "events-ingested" outbox messages and recomputes <see cref="DeliverySummary"/> rows for every day they touch
/// (the receipt day and the day before, for late device clocks). Recompute-then-replace keeps it idempotent.
/// </summary>
public sealed partial class DeliveryAggregator(TypriteDbContext db, TimeProvider time, ILogger<DeliveryAggregator> log)
{
    private const int MaxMessagesPerRun = 500;

    /// <returns>Number of outbox messages consumed.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var pending = await db.Outbox
            .Where(o => o.PublishedAtUtc == null && o.Type == "events-ingested")
            .OrderBy(o => o.Id)
            .Take(MaxMessagesPerRun)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return 0;
        }

        var days = new SortedSet<DateOnly>();
        foreach (var message in pending)
        {
            var received = ReceivedAt(message.PayloadJson) ?? message.CreatedAtUtc;
            var day = DateOnly.FromDateTime(received.UtcDateTime);
            days.Add(day);
            days.Add(day.AddDays(-1));
        }

        foreach (var day in days)
        {
            await RecomputeDayAsync(day, ct);
        }

        var now = time.GetUtcNow();
        foreach (var message in pending)
        {
            message.PublishedAtUtc = now;
            message.Attempts++;
        }

        await db.SaveChangesAsync(ct);
        LogAggregated(log, days.Count, pending.Count);
        return pending.Count;
    }

    public async Task RecomputeDayAsync(DateOnly day, CancellationToken ct)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        var facts = await db.DeliveryEvents.AsNoTracking()
            .Where(e => e.DeviceTimeUtc >= start && e.DeviceTimeUtc < end && e.DecisionId != null && e.CreativeId != null)
            .Join(db.AdDecisions.AsNoTracking(), e => e.DecisionId, d => d.Id,
                (e, d) => new RawDeliveryFact(d.CampaignId, e.CreativeId!.Value, e.DeviceId, e.Type, e.ReportedDurationMs))
            .ToListAsync(ct);

        var rows = SummaryBuilder.Build(day, facts);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.DeliverySummaries.Where(s => s.Day == day).ExecuteDeleteAsync(ct);
        db.DeliverySummaries.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static DateTimeOffset? ReceivedAt(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("receivedAtUtc", out var v) && v.TryGetDateTimeOffset(out var t) ? t : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Aggregated {Days} day(s) from {Messages} outbox message(s)")]
    private static partial void LogAggregated(ILogger logger, int days, int messages);
}
