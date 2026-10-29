namespace Typrite.Domain.Configuration;

/// <summary>Published device policy. Devices poll with ETag; the Sha256 doubles as the ETag.</summary>
public sealed class ConfigurationVersion
{
    public Guid Id { get; set; }

    public int Version { get; set; }

    /// <summary>Null means the global default; an account override wins for its devices.</summary>
    public Guid? AccountId { get; set; }

    public required string Json { get; set; }

    public required string Sha256 { get; set; }

    public required string PublishedBy { get; set; }

    public DateTimeOffset PublishedAtUtc { get; set; }
}
