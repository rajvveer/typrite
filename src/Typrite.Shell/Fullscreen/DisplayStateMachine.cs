namespace Typrite.Shell.Fullscreen;

public sealed class DisplayTransitionEventArgs(DisplayMode from, DisplayMode to) : EventArgs
{
    public DisplayMode From { get; } = from;

    public DisplayMode To { get; } = to;
}

/// <summary>
/// One instance per display. Starts in <see cref="DisplayMode.Recovering"/> because nothing is verified at startup.
/// Topology changes recover every state except safe mode, which requires explicit authorization;
/// everything else follows the transition table. Events that do not apply are ignored and reported as false, because
/// the shell is noisy and a stray notification must never corrupt state.
/// </summary>
public sealed class DisplayStateMachine
{
    public DisplayMode Mode { get; private set; } = DisplayMode.Recovering;

    public event EventHandler<DisplayTransitionEventArgs>? Transitioned;

    /// <returns>True when the mode changed.</returns>
    public bool Apply(DisplayEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var next = e.Kind switch
        {
            DisplayEventKind.TopologyChanged => Mode == DisplayMode.SafeMode ? null : DisplayMode.Recovering,
            DisplayEventKind.SafeModeRequested => DisplayMode.SafeMode,
            DisplayEventKind.Classified => Mode == DisplayMode.FullscreenDetected ? ValidateClassified(e) : null,
            _ => Next(Mode, e.Kind),
        };

        if (next is null || next == Mode)
        {
            return false;
        }

        var from = Mode;
        Mode = next.Value;
        Transitioned?.Invoke(this, new DisplayTransitionEventArgs(from, next.Value));
        return true;
    }

    private static DisplayMode? Next(DisplayMode mode, DisplayEventKind kind) => (mode, kind) switch
    {
        (DisplayMode.Recovering, DisplayEventKind.BarVerified) => DisplayMode.NormalReserved,
        (DisplayMode.NormalReserved, DisplayEventKind.FullscreenCandidateAppeared) => DisplayMode.FullscreenDetected,
        (DisplayMode.NormalReserved, DisplayEventKind.SecureDesktopAppeared) => DisplayMode.ProtectedSurface,
        (DisplayMode.CompatibleOverlay, DisplayEventKind.OverlayOccluded) => DisplayMode.YieldedFullscreen,
        (DisplayMode.CompatibleOverlay or DisplayMode.YieldedFullscreen, DisplayEventKind.FullscreenEnded) => DisplayMode.NormalReserved,
        (DisplayMode.CompatibleOverlay or DisplayMode.YieldedFullscreen, DisplayEventKind.SecureDesktopAppeared) => DisplayMode.ProtectedSurface,
        (DisplayMode.ProtectedSurface, DisplayEventKind.DesktopRestored) => DisplayMode.Recovering,
        (DisplayMode.SafeMode, DisplayEventKind.RecoveryAuthorized) => DisplayMode.Recovering,
        _ => null,
    };

    private static DisplayMode ValidateClassified(DisplayEvent e)
    {
        var mode = e.ClassifiedMode ?? throw new ArgumentException("Classified event requires a mode.", nameof(e));
        return mode is DisplayMode.NormalReserved or DisplayMode.CompatibleOverlay
            or DisplayMode.YieldedFullscreen or DisplayMode.ProtectedSurface
            ? mode
            : throw new ArgumentOutOfRangeException(nameof(e), mode, "Classification can only produce a classified mode.");
    }
}
