using Typrite.Shell.Geometry;
using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>Best-effort check that the bar is what the user actually sees at its centre point.</summary>
public static class OverlayProbe
{
    public static bool IsBarVisibleAt(nint barHwnd, PhysicalRect barBounds)
    {
        var centre = new POINT { X = barBounds.Left + barBounds.Width / 2, Y = barBounds.Top + barBounds.Height / 2 };
        var hit = WindowFromPoint(centre);
        return hit != 0 && GetAncestor(hit, GA_ROOT) == barHwnd;
    }
}
