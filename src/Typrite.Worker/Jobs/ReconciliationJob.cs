using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Typrite.Contracts;
using Typrite.Contracts.Delivery;
using Typrite.Domain.Partners;
using Typrite.Infrastructure.Partners;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Worker.Jobs;

/// <summary>
/// Compares our technically observed visibility with the partner's report for a period. The result is evidence for the
/// commercial reconciliation; it does not decide what is billable.
/// </summary>
public sealed partial class ReconciliationJob(TypriteDbContext db, IAdPartner partner, TimeProvider time, ILogger<ReconciliationJob> log)
{
    public async Task RunAsync(DateOnly periodStart, DateOnly periodEnd, double tolerance, CancellationToken ct)
    {
        var partners = await db.Partners.Where(p => p.Enabled && p.AdapterType == partner.AdapterType).ToListAsync(ct);
        if (partners.Count == 0)
        {
            return;
        }

        var ours = await db.DeliverySummaries.AsNoTracking()
            .Where(s => s.Type == DeliveryEventType.VisibilityObserved && s.Day >= periodStart && s.Day <= periodEnd)
            .Select(s => new DailyCreativeCount(s.Day, s.CreativeId, s.Count))
            .ToListAsync(ct);

        foreach (var row in partners)
        {
            var run = new ReconciliationRun
            {
                Id = Guid.NewGuid(),
                PartnerId = row.Id,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Status = ReconciliationStatus.Running,
                StartedAtUtc = time.GetUtcNow(),
            };
            db.ReconciliationRuns.Add(run);
            await db.SaveChangesAsync(ct);

            try
            {
                var theirs = (await partner.ReportAsync(periodStart, periodEnd, ct))
                    .Select(r => new DailyCreativeCount(r.Day, r.CreativeId, r.Impressions))
                    .ToList();
                var result = ReconciliationComparer.Compare(ours, theirs, tolerance);
                run.Status = result.Status;
                run.OurCount = result.OurTotal;
                run.PartnerCount = result.PartnerTotal;
                run.MismatchSummaryJson = JsonSerializer.Serialize(result.Mismatches, ContractsJson.Options);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                LogPartnerFailed(log, ex, row.Name);
                run.Status = ReconciliationStatus.Failed;
                run.MismatchSummaryJson = JsonSerializer.Serialize(new { error = ex.Message });
            }

            run.FinishedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Partner report failed for {Partner}")]
    private static partial void LogPartnerFailed(ILogger logger, Exception ex, string partner);
}
