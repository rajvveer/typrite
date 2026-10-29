using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>
/// Out-of-context WinEvent hooks for window creation, visibility, position and foreground changes.
/// Must be created on a thread that pumps messages (the UI thread). Raises <see cref="Changed"/> on that thread;
/// pass it through a <see cref="Fullscreen.QuietPeriodCoalescer"/> before doing any work.
/// </summary>
public sealed class WinEventHook : IDisposable
{
    private static readonly (uint Min, uint Max)[] Ranges =
    [
        (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
        (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
        (EVENT_OBJECT_DESTROY, EVENT_OBJECT_LOCATIONCHANGE),
    ];

    private readonly WINEVENTPROC _proc; // rooted for the hook's lifetime
    private readonly List<nint> _hooks = [];

    public WinEventHook()
    {
        _proc = OnEvent;
        foreach (var (min, max) in Ranges)
        {
            var h = SetWinEventHook(min, max, 0, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (h != 0)
            {
                _hooks.Add(h);
            }
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        foreach (var h in _hooks)
        {
            UnhookWinEvent(h);
        }

        _hooks.Clear();
    }

    private void OnEvent(nint hook, uint eventId, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // LOCATIONCHANGE fires for carets, cursors and every child; only whole top-level windows matter.
        if (hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        if (eventId is EVENT_OBJECT_LOCATIONCHANGE or EVENT_OBJECT_SHOW or EVENT_OBJECT_HIDE
            && GetAncestor(hwnd, GA_ROOT) != hwnd)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
