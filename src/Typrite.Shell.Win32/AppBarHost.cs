using Typrite.Shell.AppBar;
using Typrite.Shell.Geometry;
using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>
/// Win32 AppBar adapter. WinUI 3 does not expose the window procedure, so the HWND is subclassed to receive the
/// AppBar callback message and the activation/position messages the AppBar protocol requires.
/// All calls must come from the window's UI thread.
/// </summary>
public sealed class AppBarHost : IAppBarHost
{
    private const nuint SubclassId = 0x5459; // "TY"
    private static readonly uint CallbackMessage = RegisterWindowMessageW("Typrite.AppBar.Callback");
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

    private readonly SUBCLASSPROC _proc; // rooted for the lifetime of the subclass
    private readonly bool _allowActivation;
    private readonly bool _showInSwitchers;
    private nint _hwnd;
    private bool _registered;
    private bool _sessionNotificationsRegistered;

    public AppBarHost(bool allowActivation = false, bool showInSwitchers = false)
    {
        _proc = SubclassProc;
        _allowActivation = allowActivation;
        _showInSwitchers = showInSwitchers;
    }

    public event EventHandler<bool>? FullscreenAppChanged;

    public event EventHandler? PositionChanged;

    /// <summary>Explorer restarted; the shell forgot every AppBar. Re-register from scratch.</summary>
    public event EventHandler? ShellRestarted;

    /// <summary>WM_DPICHANGED with the new DPI.</summary>
    public event EventHandler<uint>? DpiChanged;

    /// <summary>WM_DISPLAYCHANGE: monitors added, removed, rotated or re-scaled.</summary>
    public event EventHandler? DisplayTopologyChanged;

    public event EventHandler? DesktopUnavailable;

    public event EventHandler? DesktopRestored;

    public PhysicalRect Register(nint hwnd, PhysicalRect monitor, int heightPx)
    {
        if (_registered)
        {
            throw new InvalidOperationException("Already registered.");
        }

        _hwnd = hwnd;
        if (!SetWindowSubclass(hwnd, _proc, SubclassId, 0))
        {
            throw new InvalidOperationException("SetWindowSubclass failed.");
        }

        if (_showInSwitchers)
        {
            // UI inspection only: tool windows are omitted by Windows automation inventories.
            var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)((style & ~WS_EX_TOOLWINDOW) | 0x00040000)); // WS_EX_APPWINDOW
        }
        else
        {
            ApplyExStyle(hwnd, WS_EX_TOOLWINDOW | (_allowActivation ? 0 : WS_EX_NOACTIVATE));
        }

        // Re-run WM_NCCALCSIZE through the subclass so the frame WinUI leaves behind is dropped.
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        var data = NewData();
        data.uCallbackMessage = CallbackMessage;
        if (SHAppBarMessage(ABM_NEW, ref data) == 0)
        {
            RemoveWindowSubclass(hwnd, _proc, SubclassId);
            throw new InvalidOperationException("ABM_NEW failed.");
        }

        _registered = true;
        RegisterSessionNotifications();
        return Reposition(monitor, heightPx);
    }

    public PhysicalRect Reposition(PhysicalRect monitor, int heightPx)
    {
        EnsureRegistered();
        RegisterSessionNotifications();

        var data = NewData();
        data.uEdge = ABE_BOTTOM;
        data.rc = ToRect(AppBarGeometry.RequestedBottomRect(monitor, heightPx));
        SHAppBarMessage(ABM_QUERYPOS, ref data);

        var granted = AppBarGeometry.FitHeightToAdjusted(FromRect(data.rc), heightPx);
        data.rc = ToRect(granted);
        SHAppBarMessage(ABM_SETPOS, ref data);

        SetWindowPos(_hwnd, HWND_TOP, granted.Left, granted.Top, granted.Width, granted.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        return granted;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        if (_sessionNotificationsRegistered)
        {
            WTSUnRegisterSessionNotification(_hwnd);
            _sessionNotificationsRegistered = false;
        }

        RemoveWindowSubclass(_hwnd, _proc, SubclassId);
        _registered = false;
    }

    public void Dispose() => Unregister();

    private nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == CallbackMessage)
        {
            switch ((int)wParam)
            {
                case ABN_FULLSCREENAPP:
                    FullscreenAppChanged?.Invoke(this, lParam != 0);
                    break;
                case ABN_POSCHANGED:
                    PositionChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }

            return 0;
        }

        if (uMsg == TaskbarCreatedMessage)
        {
            ShellRestarted?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        switch (uMsg)
        {
            case WM_NCCALCSIZE when wParam != 0:
                // No non-client area: a borderless WinUI window still keeps a 1px dialog frame, which reads as an outline on a docked bar.
                return 0;
            case WM_WTSSESSION_CHANGE:
                switch ((int)wParam)
                {
                    case WTS_SESSION_LOCK:
                    case WTS_CONSOLE_DISCONNECT:
                    case WTS_REMOTE_DISCONNECT:
                        DesktopUnavailable?.Invoke(this, EventArgs.Empty);
                        break;
                    case WTS_SESSION_UNLOCK:
                    case WTS_CONSOLE_CONNECT:
                    case WTS_REMOTE_CONNECT:
                        DesktopRestored?.Invoke(this, EventArgs.Empty);
                        break;
                }

                break;
            case WM_POWERBROADCAST:
                if ((int)wParam == PBT_APMSUSPEND)
                {
                    DesktopUnavailable?.Invoke(this, EventArgs.Empty);
                }
                else if ((int)wParam is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND)
                {
                    DesktopRestored?.Invoke(this, EventArgs.Empty);
                }

                return 1;
            case WM_DPICHANGED:
                DpiChanged?.Invoke(this, (uint)((long)wParam >> 16) & 0xFFFF);
                break;
            case WM_DISPLAYCHANGE:
                DisplayTopologyChanged?.Invoke(this, EventArgs.Empty);
                break;
            case WM_ACTIVATE when _registered:
                Forward(ABM_ACTIVATE);
                break;
            case WM_WINDOWPOSCHANGED when _registered:
                Forward(ABM_WINDOWPOSCHANGED);
                break;
            case WM_DESTROY:
                Unregister();
                break;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void Forward(uint message)
    {
        var data = NewData();
        SHAppBarMessage(message, ref data);
    }

    private void RegisterSessionNotifications()
    {
        if (!_sessionNotificationsRegistered)
        {
            // WTS may not be ready at sign-in; retry during the bounded reconciliation tick.
            _sessionNotificationsRegistered = WTSRegisterSessionNotification(_hwnd, 0); // this session only
        }
    }

    private void EnsureRegistered()
    {
        if (!_registered)
        {
            throw new InvalidOperationException("Not registered.");
        }
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    private static void ApplyExStyle(nint hwnd, long flags)
    {
        var current = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(current | flags));
    }

    private static RECT ToRect(PhysicalRect r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    private static PhysicalRect FromRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
