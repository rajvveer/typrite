using System.Security.Cryptography;
using Typrite.Contracts.Releases;

namespace Typrite.Domain.Tests;

public class ReleaseManifestTests
{
    private static readonly ReleaseManifest Manifest = new("1.2.3", "x64", new string('a', 64), new Uri("https://cdn.example.com/typrite-1.2.3.msi"), 50);

    [Fact]
    public void Signed_manifest_verifies_with_the_publisher_key()
    {
        using var publisher = RSA.Create(3072);
        var signature = ReleaseManifestSigner.Sign(Manifest, publisher);

        Assert.True(ReleaseManifestSigner.Verify(Manifest, signature, publisher.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void Any_field_change_breaks_the_signature()
    {
        using var publisher = RSA.Create(3072);
        var signature = ReleaseManifestSigner.Sign(Manifest, publisher);
        var pem = publisher.ExportSubjectPublicKeyInfoPem();

        Assert.False(ReleaseManifestSigner.Verify(Manifest with { PackageSha256 = new string('b', 64) }, signature, pem));
        Assert.False(ReleaseManifestSigner.Verify(Manifest with { PackageUrl = new Uri("https://evil.example.com/x.msi") }, signature, pem));
        Assert.False(ReleaseManifestSigner.Verify(Manifest with { Version = "9.9.9" }, signature, pem));
    }

    [Fact]
    public void Wrong_publisher_fails()
    {
        using var publisher = RSA.Create(3072);
        using var impostor = RSA.Create(3072);
        var signature = ReleaseManifestSigner.Sign(Manifest, publisher);

        Assert.False(ReleaseManifestSigner.Verify(Manifest, signature, impostor.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void Canonical_bytes_are_stable_regardless_of_construction()
    {
        var a = ReleaseManifestSigner.Canonical(Manifest);
        var b = ReleaseManifestSigner.Canonical(new ReleaseManifest("1.2.3", "x64", new string('a', 64), new Uri("https://cdn.example.com/typrite-1.2.3.msi"), 50));

        Assert.Equal(a, b);
    }
}
