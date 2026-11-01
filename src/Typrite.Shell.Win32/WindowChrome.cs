using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>Windows 11 rounds corners and draws a 1px border on top-level windows; a docked bar wants neither.</summary>
public static class WindowChrome
{
    /// <summary>Best effort: Windows 10 does not know these attributes and rejects them, which leaves its square frame as is.</summary>
    public static void MakeFlush(nint hwnd)
    {
        var corner = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var border = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }
}
