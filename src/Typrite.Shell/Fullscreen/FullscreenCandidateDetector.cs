using Typrite.Shell.Geometry;

namespace Typrite.Shell.Fullscreen;

public static class FullscreenCandidateDetector
{
    /// <summary>
    /// A window is a full-screen candidate when it is a real, visible, uncloaked top-level app window whose bounds
    /// cover the whole monitor (not the work area, which already excludes the bar).
    /// </summary>
    public static bool IsCandidate(WindowObservation window, PhysicalRect monitorBounds, int tolerancePx)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.IsVisible || window.IsCloaked || window.IsMinimized
            || window.IsToolWindow || window.IsOwnWindow || window.IsShellWindow)
        {
            return false;
        }

        return window.Bounds.Covers(monitorBounds, tolerancePx);
    }
}
