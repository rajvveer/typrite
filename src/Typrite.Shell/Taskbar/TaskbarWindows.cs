using Typrite.Shell.Fullscreen;

namespace Typrite.Shell.Taskbar;

/// <summary>Windows eligible for the demo task switcher, including minimized applications.</summary>
public static class TaskbarWindows
{
    public static IEnumerable<WindowObservation> Select(IEnumerable<WindowObservation> windows) =>
        windows.Where(window => window.IsVisible && !window.IsCloaked && !window.IsToolWindow
            && !window.IsOwnWindow && !window.IsShellWindow);
}
