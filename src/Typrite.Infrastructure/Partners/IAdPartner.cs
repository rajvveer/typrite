using Typrite.Domain.Campaigns;

namespace Typrite.Infrastructure.Partners;

/// <summary>What a partner said about one decision. The creative must already exist in our catalog (server-normalised).</summary>
public sealed record PartnerDecision(Guid CampaignId, Guid CreativeId, string PartnerReference);

/// <summary>Partner-side count for one creative on one day, used by reconciliation.</summary>
public sealed record PartnerDailyCount(DateOnly Day, Guid CreativeId, long Impressions);

/// <summary>Adapter contract for the one approved programmatic partner. Billing rules come from the partner contract, never from here.</summary>
public interface IAdPartner
{
    string AdapterType { get; }

    Task<PartnerDecision?> DecideAsync(Guid deviceId, string displayKey, IReadOnlyList<(Campaign Campaign, IReadOnlyList<Creative> Creatives)> inventory, CancellationToken ct);

    Task<IReadOnlyList<PartnerDailyCount>> ReportAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}
