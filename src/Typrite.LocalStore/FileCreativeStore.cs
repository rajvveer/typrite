using Typrite.Player.Creatives;

namespace Typrite.LocalStore;

/// <summary>Creatives as <c>{id}.bin</c> files; writes go through a temp file and an atomic rename.</summary>
public sealed class FileCreativeStore : ICreativeStore
{
    private const string Extension = ".bin";
    private readonly string _dir;

    public FileCreativeStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    public IReadOnlyList<StoredCreative> List()
    {
        var result = new List<StoredCreative>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*" + Extension))
        {
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id))
            {
                result.Add(new StoredCreative(id, new FileInfo(file).Length));
            }
        }

        return result;
    }

    public async Task WriteAsync(Guid id, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var final = PathFor(id);
        var temp = final + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        File.Move(temp, final, overwrite: true);
    }

    public async Task<byte[]?> ReadAsync(Guid id, CancellationToken ct)
    {
        var path = PathFor(id);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
    }

    public void Delete(Guid id) => File.Delete(PathFor(id));

    private string PathFor(Guid id) => Path.Combine(_dir, id.ToString("N") + Extension);
}
