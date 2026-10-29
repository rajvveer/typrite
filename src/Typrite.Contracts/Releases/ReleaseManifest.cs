using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Typrite.Contracts.Releases;

/// <summary>What the agent needs to install an update. Signed by the publisher key, verified on device before download.</summary>
public sealed record ReleaseManifest(string Version, string CpuArchitecture, string PackageSha256, Uri PackageUrl, int RolloutPercent);

public sealed record CurrentReleaseResponse(ReleaseManifest Manifest, string SignatureBase64);

public static class ReleaseManifestSigner
{
    /// <summary>Fixed field order, no JSON, so signing does not depend on serializer settings.</summary>
    public static byte[] Canonical(ReleaseManifest m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var text = string.Join('\n',
            "typrite-release-v1",
            m.Version,
            m.CpuArchitecture,
            m.PackageSha256.ToLowerInvariant(),
            m.PackageUrl.AbsoluteUri,
            m.RolloutPercent.ToString(CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(text);
    }

    public static string Sign(ReleaseManifest m, RSA publisherPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(publisherPrivateKey);
        return Convert.ToBase64String(publisherPrivateKey.SignData(Canonical(m), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    public static bool Verify(ReleaseManifest m, string signatureBase64, string publisherPublicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publisherPublicKeyPem);
            return rsa.VerifyData(Canonical(m), Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
