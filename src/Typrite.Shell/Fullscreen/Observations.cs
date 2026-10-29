using Typrite.Shell.Geometry;

namespace Typrite.Shell.Fullscreen;

/// <summary>A snapshot of one top-level window as reported by the Win32 adapter.</summary>
public sealed record WindowObservation(
    nint Handle,
    PhysicalRect Bounds,
    bool IsVisible,
    bool IsCloaked,
    bool IsMinimized,
    bool IsToolWindow,
    bool IsOwnWindow,
    bool IsShellWindow,
    AppIdentity? App);

/// <summary>What the detector observed on one display, ready for classification.</summary>
public sealed record DisplayObservation(
    bool IsProtectedOrSecureContext,
    WindowObservation? FullscreenCandidate,
    bool IsBorderlessCompatible,
    bool OverlayValidationPassed);
