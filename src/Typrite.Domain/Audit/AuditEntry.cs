namespace Typrite.Domain.Audit;

public sealed class AuditEntry
{
    public long Id { get; set; }

    public required string Actor { get; set; }

    public Guid? AccountId { get; set; }

    public required string Action { get; set; }

    public required string Resource { get; set; }

    public DateTimeOffset TimeUtc { get; set; }

    public string? MetadataJson { get; set; }
}

/// <summary>Transactional outbox: written in the same transaction as the change it announces, published by the worker.</summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }

    public required string Type { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public int Attempts { get; set; }
}
