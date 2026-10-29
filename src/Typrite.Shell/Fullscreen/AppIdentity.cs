namespace Typrite.Shell.Fullscreen;

/// <summary>Identity used for the overlay allowlist: executable path plus Authenticode signer. Window titles and process names are not identities.</summary>
public sealed record AppIdentity(string ExecutablePath, string? SignerSubject);

/// <summary>Path is compared case-insensitively (NTFS); the signer subject is compared exactly.</summary>
public sealed class AppIdentityComparer : IEqualityComparer<AppIdentity>
{
    public static AppIdentityComparer Instance { get; } = new();

    public bool Equals(AppIdentity? x, AppIdentity? y) =>
        x is not null && y is not null
        && string.Equals(x.ExecutablePath, y.ExecutablePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.SignerSubject, y.SignerSubject, StringComparison.Ordinal);

    public int GetHashCode(AppIdentity obj) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ExecutablePath),
            obj.SignerSubject is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.SignerSubject));
}
