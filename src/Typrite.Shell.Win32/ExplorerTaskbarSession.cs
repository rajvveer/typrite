using System.Runtime.InteropServices;
using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>Temporarily hides Explorer taskbars for the demo and restores their original state on disposal.</summary>
public sealed class ExplorerTaskbarSession : IDisposable
{
    private readonly Dictionary<nint, bool> _visibility = [];
    private readonly uint _originalState;
    private bool _disposed;

    public ExplorerTaskbarSession()
    {
        var data = Data();
        _originalState = (uint)SHAppBarMessage(ABM_GETSTATE, ref data);
        try
        {
            Suppress();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Reapply after an Explorer restart or monitor change, without recapturing the user's setting.</summary>
    public void Suppress()
    {
        lock (_visibility)
        {
            if (_disposed)
            {
                return;
            }

            var taskbars = Taskbars();
            if (taskbars.Count == 0)
            {
                throw new InvalidOperationException("Windows taskbar is unavailable for demo mode.");
            }

            foreach (var hwnd in taskbars)
            {
                _visibility.TryAdd(hwnd, IsWindowVisible(hwnd));
            }

            // Auto-hide releases Explorer's work-area reservation before TYPRITE requests its rectangle.
            var data = Data(taskbars[0]);
            var currentState = (uint)SHAppBarMessage(ABM_GETSTATE, ref data);
            if ((currentState & ABS_AUTOHIDE) == 0)
            {
                data.lParam = (nint)(currentState | ABS_AUTOHIDE);
                SHAppBarMessage(ABM_SETSTATE, ref data);
            }
            if (((uint)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) == 0)
            {
                throw new InvalidOperationException("Windows did not enable taskbar auto-hide.");
            }

            foreach (var hwnd in taskbars)
            {
                ShowWindow(hwnd, 0); // SW_HIDE
                if (IsWindowVisible(hwnd))
                {
                    throw new InvalidOperationException("Windows did not hide the taskbar.");
                }
            }
        }
    }

    // shortcut: forced termination skips cleanup; use managed shell deployment for production.
    public void Dispose()
    {
        lock (_visibility)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var taskbars = Taskbars();
            if (taskbars.Count == 0)
            {
                return;
            }

            var data = Data(taskbars[0]);
            data.lParam = (nint)_originalState;
            SHAppBarMessage(ABM_SETSTATE, ref data);
            foreach (var hwnd in taskbars)
            {
                var show = !_visibility.TryGetValue(hwnd, out var wasVisible) || wasVisible;
                ShowWindowAsync(hwnd, show ? 4 : 0); // SW_SHOWNOACTIVATE / SW_HIDE
            }
        }
    }

    private static APPBARDATA Data(nint hwnd = 0) => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = hwnd,
    };

    private static List<nint> Taskbars()
    {
        var taskbars = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            Span<char> buffer = stackalloc char[64];
            var length = GetClassName(hwnd, buffer, buffer.Length);
            var className = length > 0 ? new string(buffer[..length]) : string.Empty;
            if (className == "Shell_TrayWnd")
            {
                taskbars.Insert(0, hwnd);
            }
            else if (className == "Shell_SecondaryTrayWnd")
            {
                taskbars.Add(hwnd);
            }

            return true;
        }, 0);
        return taskbars;
    }
}
