namespace Typrite.Player.Creatives;

/// <summary>Bounded LRU over an <see cref="ICreativeStore"/>. Index is rebuilt from the store at startup.</summary>
public sealed class CreativeCache : IDisposable
{
    private readonly ICreativeStore _store;
    private readonly long _maxBytes;
    private readonly LinkedList<Guid> _lru = new(); // most recent at the tail
    private readonly Dictionary<Guid, (LinkedListNode<Guid> Node, long Size)> _index = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CreativeCache(ICreativeStore store, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
        _store = store;
        _maxBytes = maxBytes;

        foreach (var entry in store.List())
        {
            _index[entry.Id] = (_lru.AddLast(entry.Id), entry.SizeBytes);
            UsedBytes += entry.SizeBytes;
        }
    }

    public long UsedBytes { get; private set; }

    public void Dispose() => _gate.Dispose();

    public async Task<byte[]?> GetAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_index.TryGetValue(id, out var entry))
            {
                return null;
            }

            _lru.Remove(entry.Node);
            _lru.AddLast(entry.Node);
            return await _store.ReadAsync(id, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PutAsync(Guid id, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (bytes.Length > _maxBytes)
        {
            throw new InvalidOperationException($"Creative of {bytes.Length} bytes exceeds cache limit {_maxBytes}.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_index.TryGetValue(id, out var existing))
            {
                _lru.Remove(existing.Node);
                _index.Remove(id);
                UsedBytes -= existing.Size;
            }

            while (UsedBytes + bytes.Length > _maxBytes && _lru.First is { } oldest)
            {
                _store.Delete(oldest.Value);
                UsedBytes -= _index[oldest.Value].Size;
                _index.Remove(oldest.Value);
                _lru.RemoveFirst();
            }

            await _store.WriteAsync(id, bytes, ct).ConfigureAwait(false);
            _index[id] = (_lru.AddLast(id), bytes.Length);
            UsedBytes += bytes.Length;
        }
        finally
        {
            _gate.Release();
        }
    }
}
