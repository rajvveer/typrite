using System.Text.Json;
using Typrite.Contracts;
using Typrite.Domain.Audit;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api;

/// <summary>Adds an audit row to the current unit of work; the caller's SaveChanges commits it with the change it describes.</summary>
public static class AuditWriter
{
    public static void Record(TypriteDbContext db, TimeProvider time, string actor, Guid? accountId, string action, string resource, object? metadata = null) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Actor = actor,
            AccountId = accountId,
            Action = action,
            Resource = resource,
            TimeUtc = time.GetUtcNow(),
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, ContractsJson.Options),
        });
}
