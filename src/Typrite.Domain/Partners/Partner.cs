namespace Typrite.Domain.Partners;

public sealed class Partner
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Which adapter handles this partner, for example "mock".</summary>
    public required string AdapterType { get; set; }

    public bool Enabled { get; set; }
}

public enum ReconciliationStatus
{
    Running,
    Matched,
    Mismatched,
    Failed,
}

public sealed class ReconciliationRun
{
    public Guid Id { get; set; }

    public Guid PartnerId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public ReconciliationStatus Status { get; set; }

    public long OurCount { get; set; }

    public long PartnerCount { get; set; }

    public string? MismatchSummaryJson { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }
}
