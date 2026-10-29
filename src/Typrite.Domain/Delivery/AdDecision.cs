using Typrite.Contracts.Delivery;

namespace Typrite.Domain.Delivery;

public sealed class AdDecision
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public required string DisplayKey { get; set; }

    public required string PlacementId { get; set; }

    public Guid CampaignId { get; set; }

    public Guid CreativeId { get; set; }

    public required string IdempotencyKey { get; set; }

    public Guid? PartnerId { get; set; }

    public string? PartnerReference { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary>Raw, immutable technical event as received. Aggregation happens in the worker, never here.</summary>
public sealed class DeliveryEventRecord
{
    public long Id { get; set; }

    public Guid ClientEventId { get; set; }

    public Guid DeviceId { get; set; }

    public required string SessionId { get; set; }

    public required string DisplayKey { get; set; }

    public Guid? DecisionId { get; set; }

    public Guid? CreativeId { get; set; }

    public DeliveryEventType Type { get; set; }

    public DateTimeOffset DeviceTimeUtc { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public long? ReportedDurationMs { get; set; }

    public int ConfigurationVersion { get; set; }

    public int SchemaVersion { get; set; }
}

/// <summary>Daily roll-up per creative and event type, built by the worker from <see cref="DeliveryEventRecord"/>.</summary>
public sealed class DeliverySummary
{
    public long Id { get; set; }

    public DateOnly Day { get; set; }

    public Guid CampaignId { get; set; }

    public Guid CreativeId { get; set; }

    public DeliveryEventType Type { get; set; }

    public long Count { get; set; }

    public long DistinctDevices { get; set; }

    public long VisibleMilliseconds { get; set; }
}
