namespace Typrite.Shell.Fullscreen;

/// <summary>Per-display mode. Visibility accounting and ad playback key off this.</summary>
public enum DisplayMode
{
    /// <summary>AppBar registered; maximized apps avoid it.</summary>
    NormalReserved,

    /// <summary>A candidate full-screen window was seen; classification pending.</summary>
    FullscreenDetected,

    /// <summary>Test-gated overlay above an allowlisted borderless app.</summary>
    CompatibleOverlay,

    /// <summary>Bar dropped behind a full-screen app; no visibility claims.</summary>
    YieldedFullscreen,

    /// <summary>Secure desktop, lock screen or protected content; ads suspended.</summary>
    ProtectedSurface,

    /// <summary>Re-registering after topology change, Explorer restart or wake.</summary>
    Recovering,

    /// <summary>Ads and overlay suppressed until an administrator authorizes recovery.</summary>
    SafeMode,
}
