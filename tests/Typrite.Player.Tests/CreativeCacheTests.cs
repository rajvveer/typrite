using Typrite.Player.Creatives;

namespace Typrite.Player.Tests;

public class CreativeCacheTests
{
    private sealed class MemoryStore : ICreativeStore
    {
        public Dictionary<Guid, byte[]> Files { get; } = [];

        public IReadOnlyList<StoredCreative> List() => [.. Files.Select(f => new StoredCreative(f.Key, f.Value.Length))];

        public Task WriteAsync(Guid id, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            Files[id] = bytes.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(id));

        public void Delete(Guid id) => Files.Remove(id);
    }

    private static byte[] Bytes(int n) => new byte[n];

    [Fact]
    public async Task Put_then_get_returns_bytes()
    {
        var store = new MemoryStore();
        var cache = new CreativeCache(store, maxBytes: 100);
        var id = Guid.NewGuid();

        await cache.PutAsync(id, Bytes(10), CancellationToken.None);

        Assert.NotNull(await cache.GetAsync(id, CancellationToken.None));
        Assert.Equal(10, cache.UsedBytes);
    }

    [Fact]
    public async Task Evicts_least_recently_used_until_under_limit()
    {
        var store = new MemoryStore();
        var cache = new CreativeCache(store, maxBytes: 100);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await cache.PutAsync(a, Bytes(40), CancellationToken.None);
        await cache.PutAsync(b, Bytes(40), CancellationToken.None);
        await cache.GetAsync(a, CancellationToken.None); // a is now more recent than b

        await cache.PutAsync(c, Bytes(40), CancellationToken.None);

        Assert.False(store.Files.ContainsKey(b));
        Assert.True(store.Files.ContainsKey(a));
        Assert.True(store.Files.ContainsKey(c));
        Assert.Equal(80, cache.UsedBytes);
    }

    [Fact]
    public async Task Rejects_creative_larger_than_the_whole_cache()
    {
        var cache = new CreativeCache(new MemoryStore(), maxBytes: 100);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.PutAsync(Guid.NewGuid(), Bytes(101), CancellationToken.None));
    }

    [Fact]
    public async Task Rebuilds_index_from_store_on_startup()
    {
        var store = new MemoryStore();
        var existing = Guid.NewGuid();
        store.Files[existing] = Bytes(30);

        var cache = new CreativeCache(store, maxBytes: 100);

        Assert.Equal(30, cache.UsedBytes);
        Assert.NotNull(await cache.GetAsync(existing, CancellationToken.None));
    }

    [Fact]
    public async Task Get_of_missing_id_returns_null_without_touching_index()
    {
        var cache = new CreativeCache(new MemoryStore(), maxBytes: 100);

        Assert.Null(await cache.GetAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, cache.UsedBytes);
    }
}
