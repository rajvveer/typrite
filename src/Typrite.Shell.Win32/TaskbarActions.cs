using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>User-initiated window switching for the demo taskbar; never bypasses foreground restrictions.</summary>
public static class TaskbarActions
{
    public static string Title(nint hwnd)
    {
        Span<char> buffer = stackalloc char[512];
        var length = GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    public static bool Activate(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return false;
        }

        if (IsIconic(hwnd))
        {
            ShowWindowAsync(hwnd, 9); // SW_RESTORE
        }

        return SetForegroundWindow(hwnd);
    }
}
