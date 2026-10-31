# Taskbar demo

On Windows 11 x64, build the WinUI project and start its generated executable with:

```powershell
Typrite.Desktop.exe --taskbar-demo
```

Close any existing TYPRITE instance first; the application allows one instance per session. Without the flag, the existing advertising bar runs as before.

The navigation row sits above the advertising strip and adds 48 DIPs to the workspace reservation. It offers a Start-style pinned-app menu, File Explorer and Edge launchers, a running-window switcher (including minimized apps), Settings, and local clock/date. Unavailable apps and failed window switches show a status message. Start → Exit demo closes all TYPRITE bars and releases their reservations.

Demo mode automatically saves the Windows taskbar's auto-hide state, enables auto-hide to release its workspace reservation, and hides the primary and secondary taskbars before creating TYPRITE bars. It reapplies suppression after Explorer restarts and display rebuilds. Closing the demo restores the previous state and visibility; startup failures and unhandled application exceptions also attempt restoration. Normal mode does not change taskbar settings.

Entering TYPRITE safe mode restores the Windows taskbar; authorized recovery hides it again.

This is an unlocked interactive demo, not an exact Windows taskbar replacement. Search, native application icons/grouping, notification tray icons, system flyouts, and virtual desktop controls are not implemented. Explorer remains running. Windows shortcuts or shell behavior can reveal its UI again; this is temporary hiding, not a kiosk policy or a bypass-prevention guarantee. There is no continuous positioning or hiding loop.

Forced termination, power loss, or an OS crash can skip restoration. If the taskbar stays hidden, restart **Windows Explorer** through Task Manager, then restore **Settings → Personalization → Taskbar → Taskbar behaviors → Automatically hide the taskbar** to your previous preference. Test this on a disposable Windows VM before using a personal desktop.

## Windows verification

- Start with and without the flag; verify only demo mode adds the navigation row.
- With taskbar auto-hide initially off and then on, verify demo startup hides primary/secondary taskbars and exit restores the original setting. Restart Explorer and attach a display during the demo; check suppression and exit restoration again.
- Open each launcher, navigate the menu by keyboard, and check Narrator labels and light/dark themes.
- Switch to an ordinary window and restore a minimized window. Close an app while its menu entry is open; a failed switch must not crash the bar.
- Verify maximize, typing, Alt+Tab, 100/125/150% DPI, secondary displays, and full-screen yield/recovery.
- Exit through Start → Exit demo; confirm all bars close and the work area is restored.

No Windows visual or interaction validation has been performed on the development Mac.

To automate the basic startup/close restoration check on Windows (with no other TYPRITE instance running):

```powershell
.\tools\taskbar-smoke.ps1 -DesktopExe 'C:\path\to\Typrite.Desktop.exe'
```

The script checks taskbar visibility and auto-hide state, closes TYPRITE's windows, verifies restoration, and restores the captured taskbar state in its own cleanup if the test fails. It does not test crash recovery, Explorer restart, or monitor changes.

## Managed deployment still required

The production target is workspace → custom taskbar → TYPRITE → screen bottom. A replacement shell needs separate workspace management validated without Explorer, full agreed taskbar functionality, and deployment on a confirmed Windows edition. The demo's Explorer-dependent AppBar is not that implementation.

[Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/) supports Enterprise, Education, and IoT Enterprise editions. It replaces the managed account's shell but does not itself stop users accessing other applications. Use standard-user accounts, an enforced application allowlist, and scoped Windows policies, plus a separate administrator recovery account and tested rollback. Administrators and physical device access are outside any absolute no-bypass guarantee.
