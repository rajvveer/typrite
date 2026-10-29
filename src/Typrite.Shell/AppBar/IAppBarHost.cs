using Typrite.Shell.Geometry;

namespace Typrite.Shell.AppBar;

/// <summary>Shell-reservation port. One instance per bar window. Implemented by the Win32 adapter; faked in tests.</summary>
public interface IAppBarHost : IDisposable
{
    /// <summary>Registers the window as a bottom-edge AppBar on <paramref name="monitor"/> and returns the rect the shell granted.</summary>
    PhysicalRect Register(nint hwnd, PhysicalRect monitor, int heightPx);

    /// <summary>Re-negotiates position after ABN_POSCHANGED or a DPI change. Returns the granted rect.</summary>
    PhysicalRect Reposition(PhysicalRect monitor, int heightPx);

    /// <summary>ABM_REMOVE. Safe to call more than once.</summary>
    void Unregister();

    /// <summary>ABN_FULLSCREENAPP: true when a full-screen app opened on this bar's monitor, false when it closed.</summary>
    event EventHandler<bool>? FullscreenAppChanged;

    /// <summary>ABN_POSCHANGED: taskbar moved/resized or another AppBar changed; caller should <see cref="Reposition"/>.</summary>
    event EventHandler? PositionChanged;
}
