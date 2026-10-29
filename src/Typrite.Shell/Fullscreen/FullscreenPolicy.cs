namespace Typrite.Shell.Fullscreen;

/// <summary>Overlay policy. Immutable; the default allows nothing.</summary>
public sealed class FullscreenPolicy
{
    private readonly HashSet<AppIdentity> _allowlist;

    private FullscreenPolicy(HashSet<AppIdentity> allowlist) => _allowlist = allowlist;

    public static FullscreenPolicy YieldByDefault { get; } = new([]);

    public FullscreenPolicy Allow(AppIdentity app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var next = new HashSet<AppIdentity>(_allowlist, AppIdentityComparer.Instance) { app };
        return new FullscreenPolicy(next);
    }

    /// <summary>Unsigned binaries can never be allowlisted, even if someone adds them.</summary>
    public bool IsAllowlisted(AppIdentity? app) =>
        app is { SignerSubject: not null } && _allowlist.Contains(app);
}
