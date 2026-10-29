using Typrite.LocalStore;

namespace Typrite.LocalStore.Tests;

public sealed class FileCreativeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"typrite-c-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Write_read_list_delete_round_trip()
    {
        var store = new FileCreativeStore(_dir);
        var id = Guid.NewGuid();

        await store.WriteAsync(id, new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.Equal([1, 2, 3], await store.ReadAsync(id, CancellationToken.None));
        var entry = Assert.Single(store.List());
        Assert.Equal(id, entry.Id);
        Assert.Equal(3, entry.SizeBytes);

        store.Delete(id);
        Assert.Empty(store.List());
        Assert.Null(await store.ReadAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Write_is_atomic_no_partial_file_is_listed()
    {
        var store = new FileCreativeStore(_dir);
        await store.WriteAsync(Guid.NewGuid(), new byte[10], CancellationToken.None);

        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Foreign_files_in_the_directory_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");

        Assert.Empty(new FileCreativeStore(_dir).List());
    }
}
