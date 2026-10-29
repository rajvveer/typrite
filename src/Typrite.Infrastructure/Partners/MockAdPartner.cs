using Typrite.Domain.Campaigns;

namespace Typrite.Infrastructure.Partners;

/// <summary>
/// Stand-in until the real partner contract and sandbox exist. Decides from our own inventory and "reports" whatever
/// counts the test or operator seeded through <see cref="SeedReport"/>, so reconciliation can be exercised end to end.
/// </summary>
public sealed class MockAdPartner : IAdPartner
{
    private readonly List<PartnerDailyCount> _report = [];
    private readonly Lock _gate = new();

    public string AdapterType => "mock";

    public Task<PartnerDecision?> DecideAsync(Guid deviceId, string displayKey, IReadOnlyList<(Campaign Campaign, IReadOnlyList<Creative> Creatives)> inventory, CancellationToken ct)
    {
        foreach (var (campaign, creatives) in inventory)
        {
            if (campaign.Status == CampaignStatus.Active && creatives.Count > 0)
            {
                return Task.FromResult<PartnerDecision?>(new PartnerDecision(campaign.Id, creatives[0].Id, $"mock-{Guid.NewGuid():N}"));
            }
        }

        return Task.FromResult<PartnerDecision?>(null);
    }

    public Task<IReadOnlyList<PartnerDailyCount>> ReportAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PartnerDailyCount>>([.. _report.Where(r => r.Day >= periodStart && r.Day <= periodEnd)]);
        }
    }

    public void SeedReport(IEnumerable<PartnerDailyCount> counts)
    {
        lock (_gate)
        {
            _report.AddRange(counts);
        }
    }
}
