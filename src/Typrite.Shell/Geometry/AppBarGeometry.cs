namespace Typrite.Shell.Geometry;

/// <summary>Rectangle math for a bottom-edge AppBar. Pure; the Win32 adapter feeds it monitor bounds and the shell's adjusted rect.</summary>
public static class AppBarGeometry
{
    /// <summary>The rect to request with ABM_QUERYPOS: full monitor width, pinned to the bottom edge.</summary>
    public static PhysicalRect RequestedBottomRect(PhysicalRect monitor, double reservationDip, uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reservationDip, 0);
        var height = DpiScale.DipToPhysical(reservationDip, dpi);
        return new PhysicalRect(monitor.Left, monitor.Bottom - height, monitor.Right, monitor.Bottom);
    }

    /// <summary>
    /// After ABM_QUERYPOS the shell may move the rect (for example above the taskbar). Keep the shell's bottom edge
    /// and re-apply our height so the bar keeps its size, then pass the result to ABM_SETPOS.
    /// </summary>
    public static PhysicalRect FitHeightToAdjusted(PhysicalRect adjusted, int heightPx)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(heightPx, 0);
        return adjusted with { Top = adjusted.Bottom - heightPx };
    }
}
