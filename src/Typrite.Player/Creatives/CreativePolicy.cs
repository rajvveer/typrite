namespace Typrite.Player.Creatives;

/// <summary>What the player will accept. Codec policy per the guide: H.264 MP4 and PNG/JPEG/WebP only.</summary>
public sealed record CreativePolicy(
    IReadOnlySet<string> AllowedFormats,
    IReadOnlySet<string> AllowedMediaTypes,
    long MaxImageBytes,
    long MaxVideoBytes,
    int MaxVideoDurationMs)
{
    public static CreativePolicy Default { get; } = new(
        new HashSet<string>(StringComparer.Ordinal) { "400x225", "384x216", "300x250" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "video/mp4" },
        MaxImageBytes: 2 * 1024 * 1024,
        MaxVideoBytes: 20 * 1024 * 1024,
        MaxVideoDurationMs: 30_000);

    public static bool IsVideo(string mediaType) => mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
}
