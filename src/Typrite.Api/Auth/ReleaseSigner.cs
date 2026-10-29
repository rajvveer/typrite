using System.Security.Cryptography;
using Typrite.Contracts.Releases;

namespace Typrite.Api.Auth;

public sealed record ReleaseOptions
{
    /// <summary>PKCS#8 or RSA PEM of the publisher key. Generated per run when empty, which is only acceptable for development.</summary>
    public string? PublisherPrivateKeyPem { get; init; }
}

/// <summary>Signs release manifests with the publisher key; the agent pins the matching public key.</summary>
public sealed class ReleaseSigner : IDisposable
{
    private readonly RSA _key;

    public ReleaseSigner(ReleaseOptions options)
    {
        _key = RSA.Create(3072);
        if (!string.IsNullOrWhiteSpace(options.PublisherPrivateKeyPem))
        {
            _key.ImportFromPem(options.PublisherPrivateKeyPem);
        }

        PublicKeyPem = _key.ExportSubjectPublicKeyInfoPem();
    }

    public string PublicKeyPem { get; }

    public string Sign(ReleaseManifest manifest) => ReleaseManifestSigner.Sign(manifest, _key);

    public void Dispose() => _key.Dispose();
}
