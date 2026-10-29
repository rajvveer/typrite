namespace Typrite.Domain.Devices;

public enum DeviceCredentialKind
{
    /// <summary>Development-only shared secret stored as a hash. Rejected once key-based enrollment is enforced.</summary>
    Dev,

    /// <summary>Per-device asymmetric key held by the agent.</summary>
    Key,
}

public enum DeviceStatus
{
    Active,
    Deactivated,
}

public sealed class Device
{
    public Guid Id { get; set; }

    public Guid? AccountId { get; set; }

    public DeviceCredentialKind CredentialKind { get; set; }

    /// <summary>SHA-256 of the dev secret, or SHA-256 thumbprint of the device public key.</summary>
    public required string CredentialHash { get; set; }

    /// <summary>SubjectPublicKeyInfo, base64, for <see cref="DeviceCredentialKind.Key"/>.</summary>
    public string? PublicKey { get; set; }

    public DeviceStatus Status { get; set; } = DeviceStatus.Active;

    public string? AppVersion { get; set; }

    public string? CpuArchitecture { get; set; }

    public int ConfigurationVersion { get; set; }

    public DateTimeOffset EnrolledAtUtc { get; set; }

    public DateTimeOffset? LastSeenUtc { get; set; }

    public string? LastHealthJson { get; set; }
}

public enum CommandStatus
{
    Pending,
    Acknowledged,
    Expired,
    Cancelled,
}

/// <summary>Authorized, expiring remote action. Allowlisted types only; see <see cref="RemoteCommandTypes"/>.</summary>
public sealed class RemoteCommand
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public required string Type { get; set; }

    public string? PayloadJson { get; set; }

    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    public required string RequestedBy { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; set; }

    public string? ResultJson { get; set; }
}

public static class RemoteCommandTypes
{
    public const string RefreshConfiguration = "refresh-configuration";
    public const string EnterSafeMode = "enter-safe-mode";
    public const string LeaveSafeMode = "leave-safe-mode";
    public const string UploadDiagnostics = "upload-diagnostics";
    public const string RestartBar = "restart-bar";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        RefreshConfiguration, EnterSafeMode, LeaveSafeMode, UploadDiagnostics, RestartBar,
    };
}

/// <summary>One-time claim token handed to an installer or OEM workflow; binds the enrolling device to an account.</summary>
public sealed class EnrollmentClaim
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public Guid? UsedByDeviceId { get; set; }

    public DateTimeOffset? UsedAtUtc { get; set; }

    public required string IssuedBy { get; set; }
}
