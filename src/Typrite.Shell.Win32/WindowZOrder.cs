using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>Z-order moves used when yielding to and returning from full-screen apps. Never activates the window.</summary>
public static class WindowZOrder
{
    private const uint Flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;

    public static void SendToBottom(nint hwnd) => SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, Flags);

    public static void BringToTop(nint hwnd) => SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, Flags);

    public static uint DpiOf(nint hwnd) => GetDpiForWindow(hwnd);
}
