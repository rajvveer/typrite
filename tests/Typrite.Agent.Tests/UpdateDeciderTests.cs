using System.Security.Cryptography;
using Typrite.Agent.Updates;
using Typrite.Contracts.Releases;

namespace Typrite.Agent.Tests;

public class UpdateDeciderTests
{
    private static readonly RSA Publisher = RSA.Create(3072);
    private static readonly string PublisherPem = Publisher.ExportSubjectPublicKeyInfoPem();

    private static CurrentReleaseResponse Signed(string version, string arch = "x64", RSA? signer = null)
    {
        var m = new ReleaseManifest(version, arch, new string('a', 64), new Uri("https://cdn.example.com/t.msi"), 100);
        return new CurrentReleaseResponse(m, ReleaseManifestSigner.Sign(m, signer ?? Publisher));
    }

    [Fact]
    public void Newer_signed_release_for_this_architecture_is_installable()
    {
        var d = UpdateDecider.Decide(Signed("1.2.0"), installedVersion: "1.1.9", architecture: "x64", PublisherPem);

        Assert.Equal(UpdateAction.Install, d.Action);
    }

    [Fact]
    public void Same_or_older_version_is_skipped()
    {
        Assert.Equal(UpdateAction.UpToDate, UpdateDecider.Decide(Signed("1.2.0"), "1.2.0", "x64", PublisherPem).Action);
        Assert.Equal(UpdateAction.UpToDate, UpdateDecider.Decide(Signed("1.1.0"), "1.2.0", "x64", PublisherPem).Action);
    }

    [Fact]
    public void Bad_signature_is_rejected_even_if_newer()
    {
        using var impostor = RSA.Create(3072);

        var d = UpdateDecider.Decide(Signed("9.0.0", signer: impostor), "1.0.0", "x64", PublisherPem);

        Assert.Equal(UpdateAction.Rejected, d.Action);
        Assert.Contains("signature", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrong_architecture_is_rejected()
    {
        Assert.Equal(UpdateAction.Rejected, UpdateDecider.Decide(Signed("9.0.0", arch: "arm64"), "1.0.0", "x64", PublisherPem).Action);
    }

    [Fact]
    public void Non_https_package_url_is_rejected()
    {
        var m = new ReleaseManifest("9.0.0", "x64", new string('a', 64), new Uri("http://cdn.example.com/t.msi"), 100);
        var r = new CurrentReleaseResponse(m, ReleaseManifestSigner.Sign(m, Publisher));

        Assert.Equal(UpdateAction.Rejected, UpdateDecider.Decide(r, "1.0.0", "x64", PublisherPem).Action);
    }

    [Fact]
    public void Unparseable_versions_are_rejected_not_thrown()
    {
        Assert.Equal(UpdateAction.Rejected, UpdateDecider.Decide(Signed("latest"), "1.0.0", "x64", PublisherPem).Action);
    }

    [Fact]
    public void Downloaded_package_must_match_declared_hash()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var m = new ReleaseManifest("1.0.0", "x64", Convert.ToHexStringLower(SHA256.HashData(bytes)), new Uri("https://cdn.example.com/t.msi"), 100);

        Assert.True(UpdateDecider.PackageMatches(m, bytes));
        Assert.False(UpdateDecider.PackageMatches(m, [1, 2, 3, 4]));
    }
}
