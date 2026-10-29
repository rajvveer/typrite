namespace Typrite.Shell.Fullscreen;

public enum DisplayEventKind
{
    BarVerified,
    FullscreenCandidateAppeared,
    Classified,
    FullscreenEnded,
    OverlayOccluded,
    SecureDesktopAppeared,
    DesktopRestored,
    TopologyChanged,
    SafeModeRequested,
    RecoveryAuthorized,
}

/// <summary>Input to <see cref="DisplayStateMachine"/>. Only <see cref="Classified"/> carries data.</summary>
public sealed record DisplayEvent(DisplayEventKind Kind, DisplayMode? ClassifiedMode = null)
{
    public static DisplayEvent BarVerified { get; } = new(DisplayEventKind.BarVerified);
    public static DisplayEvent FullscreenCandidateAppeared { get; } = new(DisplayEventKind.FullscreenCandidateAppeared);
    public static DisplayEvent FullscreenEnded { get; } = new(DisplayEventKind.FullscreenEnded);
    public static DisplayEvent OverlayOccluded { get; } = new(DisplayEventKind.OverlayOccluded);
    public static DisplayEvent SecureDesktopAppeared { get; } = new(DisplayEventKind.SecureDesktopAppeared);
    public static DisplayEvent DesktopRestored { get; } = new(DisplayEventKind.DesktopRestored);
    public static DisplayEvent TopologyChanged { get; } = new(DisplayEventKind.TopologyChanged);
    public static DisplayEvent SafeModeRequested { get; } = new(DisplayEventKind.SafeModeRequested);
    public static DisplayEvent RecoveryAuthorized { get; } = new(DisplayEventKind.RecoveryAuthorized);

    public static DisplayEvent Classified(DisplayMode mode) => new(DisplayEventKind.Classified, mode);
}
