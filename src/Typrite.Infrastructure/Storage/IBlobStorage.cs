namespace Typrite.Infrastructure.Storage;

/// <summary>Creative byte storage. File system locally; swap for Azure Blob Storage with the same contract.</summary>
public interface IBlobStorage
{
    Task PutAsync(string key, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct);

    Task<(byte[] Bytes, string ContentType)?> GetAsync(string key, CancellationToken ct);
}

public sealed class FileSystemBlobStorage(string root) : IBlobStorage
{
    public async Task PutAsync(string key, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(path + ".type", contentType, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var type = File.Exists(path + ".type") ? await File.ReadAllTextAsync(path + ".type", ct).ConfigureAwait(false) : "application/octet-stream";
        return (bytes, type.Trim());
    }

    private string PathFor(string key)
    {
        if (key.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
