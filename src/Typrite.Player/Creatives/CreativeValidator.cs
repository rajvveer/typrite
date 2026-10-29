using System.Security.Cryptography;
using Typrite.Contracts.Delivery;

namespace Typrite.Player.Creatives;

public static class CreativeValidator
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    /// <summary>Checks the manifest before any bytes are fetched. Empty result means acceptable.</summary>
    public static IReadOnlyList<string> Validate(CreativeManifest m, CreativePolicy p)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(p);
        var problems = new List<string>();

        if (!p.AllowedFormats.Contains(m.Format))
        {
            problems.Add($"format '{m.Format}' is not in the contract");
        }

        if (!p.AllowedMediaTypes.Contains(m.MediaType))
        {
            problems.Add($"media type '{m.MediaType}' is not allowed");
        }

        var isVideo = CreativePolicy.IsVideo(m.MediaType);
        var maxBytes = isVideo ? p.MaxVideoBytes : p.MaxImageBytes;
        if (m.SizeBytes <= 0 || m.SizeBytes > maxBytes)
        {
            problems.Add($"size {m.SizeBytes} outside 1..{maxBytes}");
        }

        if (isVideo && (m.DurationMs is null or <= 0 || m.DurationMs > p.MaxVideoDurationMs))
        {
            problems.Add("video duration missing or above limit");
        }

        if (m.Sha256.Length != 64 || !m.Sha256.All(Uri.IsHexDigit))
        {
            problems.Add("sha256 must be 64 hex characters");
        }

        if (m.ContentUri is null || m.ContentUri.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add("content must be fetched over https");
        }

        if (m.ClickThroughUrl is not null
            && (!Uri.TryCreate(m.ClickThroughUrl, UriKind.Absolute, out var click) || click.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add("click-through must be an absolute https url");
        }

        return problems;
    }

    /// <summary>Checks downloaded bytes against the manifest: size, hash, and the magic bytes of the declared media type.</summary>
    public static IReadOnlyList<string> VerifyBytes(CreativeManifest m, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(m);
        var problems = new List<string>();

        if (bytes.Length != m.SizeBytes)
        {
            problems.Add($"size {bytes.Length} differs from declared {m.SizeBytes}");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!hash.Equals(m.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("sha256 mismatch");
        }

        if (!MagicMatches(m.MediaType, bytes))
        {
            problems.Add($"bytes do not look like {m.MediaType}");
        }

        return problems;
    }

    private static bool MagicMatches(string mediaType, ReadOnlySpan<byte> b) => mediaType.ToUpperInvariant() switch
    {
        "IMAGE/PNG" => b.StartsWith(PngMagic),
        "IMAGE/JPEG" => b.StartsWith(JpegMagic),
        "IMAGE/WEBP" => b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8),
        "VIDEO/MP4" => b.Length >= 12 && b[4..8].SequenceEqual("ftyp"u8),
        _ => false,
    };
}
