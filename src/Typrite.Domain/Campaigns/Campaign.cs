namespace Typrite.Domain.Campaigns;

public enum CampaignStatus
{
    Draft,
    Approved,
    Active,
    Paused,
    Ended,
}

public sealed class Campaign
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public required string Name { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    public DateTimeOffset StartsAtUtc { get; set; }

    public DateTimeOffset EndsAtUtc { get; set; }

    /// <summary>Relative share of decisions among eligible campaigns. Positive.</summary>
    public int Weight { get; set; } = 1;

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class Creative
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public required string Format { get; set; }

    public required string MediaType { get; set; }

    public long SizeBytes { get; set; }

    public required string Sha256 { get; set; }

    /// <summary>Key in blob storage; content is served through the API or a signed CDN URL, never a raw advertiser URL.</summary>
    public required string StorageKey { get; set; }

    public int? DurationMs { get; set; }

    public string? ClickThroughUrl { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class CampaignLifecycle
{
    public static bool CanTransition(CampaignStatus from, CampaignStatus to) => (from, to) switch
    {
        (CampaignStatus.Draft, CampaignStatus.Approved) => true,
        (CampaignStatus.Approved, CampaignStatus.Active) => true,
        (CampaignStatus.Active, CampaignStatus.Paused) => true,
        (CampaignStatus.Paused, CampaignStatus.Active) => true,
        (CampaignStatus.Approved or CampaignStatus.Active or CampaignStatus.Paused, CampaignStatus.Ended) => true,
        _ => false,
    };

    public static bool CanApprove(int creativeCount) => creativeCount > 0;
}
