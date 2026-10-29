using System.Security.Claims;

namespace Typrite.Api.Auth;

public static class Policies
{
    public const string Device = "Device";
    public const string Viewer = "Viewer";
    public const string CampaignOperator = "CampaignOperator";
    public const string DeviceOperator = "DeviceOperator";
    public const string ReleaseManager = "ReleaseManager";
    public const string Administrator = "Administrator";

    /// <summary>Release manager or administrator of the platform account. Releases and partner reconciliation are fleet-wide, not per tenant.</summary>
    public const string PlatformReleaseManager = "PlatformReleaseManager";
    public const string PlatformViewer = "PlatformViewer";
}

public readonly record struct DevicePrincipal(Guid DeviceId, Guid? AccountId, string CredentialKind)
{
    public static DevicePrincipal From(ClaimsPrincipal user) => new(
        Guid.Parse(user.FindFirstValue(Claims.DeviceId)!),
        Guid.TryParse(user.FindFirstValue(Claims.AccountId), out var a) ? a : null,
        user.FindFirstValue(Claims.CredentialKind) ?? "unknown");
}

public readonly record struct OperatorPrincipal(string Subject, string Role, Guid AccountId)
{
    public static OperatorPrincipal From(ClaimsPrincipal user) => new(
        user.FindFirstValue("sub") ?? user.Identity?.Name ?? "unknown",
        user.FindFirstValue(Claims.Role) ?? "Viewer",
        Guid.Parse(user.FindFirstValue(Claims.AccountId)!));
}
