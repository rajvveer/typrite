using System.Security.Cryptography;

namespace Typrite.Domain.Releases;

public enum ReleaseStatus
{
    Staged,
    RollingOut,
    Paused,
    RolledBack,
    Complete,
}

public sealed class Release
{
    public Guid Id { get; set; }

    public required string Version { get; set; }

    public required string CpuArchitecture { get; set; }

    /// <summary>SHA-256 of the installer; the agent verifies the download against it.</summary>
    public required string PackageSha256 { get; set; }

    public required string PackageUrl { get; set; }

    /// <summary>Detached signature over the manifest JSON, base64. Verified by the agent with the pinned publisher key.</summary>
    public required string ManifestSignature { get; set; }

    public ReleaseStatus Status { get; set; } = ReleaseStatus.Staged;

    /// <summary>0..100; devices whose cohort bucket is below this get the release.</summary>
    public int RolloutPercent { get; set; }

    public required string CreatedBy { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class RolloutCohort
{
    /// <summary>Stable bucket 0..99 from the device id, so raising the percentage only ever adds devices.</summary>
    public static bool IsIncluded(Guid deviceId, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(deviceId.ToByteArray(), hash);
        var bucket = BitConverter.ToUInt32(hash) % 100;
        return bucket < percent;
    }
}
