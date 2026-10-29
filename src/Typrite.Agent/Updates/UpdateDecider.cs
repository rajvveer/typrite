using System.Security.Cryptography;
using Typrite.Contracts.Releases;

namespace Typrite.Agent.Updates;

public enum UpdateAction
{
    UpToDate,
    Install,
    Rejected,
}

public readonly record struct UpdateDecision(UpdateAction Action, string Reason);

public static class UpdateDecider
{
    /// <summary>Signature first, then architecture, then https, then a strictly newer semantic version.</summary>
    public static UpdateDecision Decide(CurrentReleaseResponse current, string installedVersion, string architecture, string publisherPublicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(current);
        var m = current.Manifest;

        if (!ReleaseManifestSigner.Verify(m, current.SignatureBase64, publisherPublicKeyPem))
        {
            return new(UpdateAction.Rejected, "manifest signature does not verify against the pinned publisher key");
        }

        if (!string.Equals(m.CpuArchitecture, architecture, StringComparison.OrdinalIgnoreCase))
        {
            return new(UpdateAction.Rejected, $"manifest is for {m.CpuArchitecture}, this device is {architecture}");
        }

        if (m.PackageUrl.Scheme != Uri.UriSchemeHttps)
        {
            return new(UpdateAction.Rejected, "package url must be https");
        }

        if (!Version.TryParse(m.Version, out var offered) || !Version.TryParse(installedVersion, out var installed))
        {
            return new(UpdateAction.Rejected, "versions must be dotted numeric");
        }

        return offered > installed
            ? new(UpdateAction.Install, $"{installed} -> {offered}")
            : new(UpdateAction.UpToDate, $"installed {installed}, offered {offered}");
    }

    public static bool PackageMatches(ReleaseManifest manifest, ReadOnlySpan<byte> package)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Convert.ToHexStringLower(SHA256.HashData(package)).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase);
    }
}
