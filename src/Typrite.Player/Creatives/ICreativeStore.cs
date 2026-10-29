namespace Typrite.Player.Creatives;

public sealed record StoredCreative(Guid Id, long SizeBytes);

/// <summary>Byte storage for validated creatives. The cache owns eviction; the store owns durability.</summary>
public interface ICreativeStore
{
    IReadOnlyList<StoredCreative> List();

    Task WriteAsync(Guid id, ReadOnlyMemory<byte> bytes, CancellationToken ct);

    Task<byte[]?> ReadAsync(Guid id, CancellationToken ct);

    void Delete(Guid id);
}
