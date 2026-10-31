# Taskbar demo

On Windows 11 x64, build the WinUI project and start its generated executable with:

```powershell
Typrite.Desktop.exe --taskbar-demo
```

Close any existing TYPRITE instance first; the application allows one instance per session. Without the flag, the existing advertising bar runs as before.

The navigation row sits above the advertising strip and adds 48 DIPs to the workspace reservation. It offers a Start-style pinned-app menu, File Explorer and Edge launchers, a running-window switcher (including minimized apps), Settings, and local clock/date. Unavailable apps and failed window switches show a status message. Start → Exit demo closes all TYPRITE bars and releases their reservations.

This is an unlocked interactive demo, not an exact Windows taskbar replacement. Search, native application icons/grouping, notification tray icons, system flyouts, and virtual desktop controls are not implemented. The original Windows taskbar stays available below TYPRITE. To demonstrate the visual order without that extra row, manually enable **Settings → Personalization → Taskbar → Taskbar behaviors → Automatically hide the taskbar**. The original taskbar can still reappear; restore the setting after the demo. TYPRITE does not change system policies or hide Explorer.

## Windows verification

- Start with and without the flag; verify only demo mode adds the navigation row.
- Open each launcher, navigate the menu by keyboard, and check Narrator labels and light/dark themes.
- Switch to an ordinary window and restore a minimized window. Close an app while its menu entry is open; a failed switch must not crash the bar.
- Verify maximize, typing, Alt+Tab, 100/125/150% DPI, secondary displays, and full-screen yield/recovery.
- Exit through Start → Exit demo; confirm all bars close and the work area is restored.

No Windows visual or interaction validation has been performed on the development Mac.

## Managed deployment still required

The production target is workspace → custom taskbar → TYPRITE → screen bottom. A replacement shell needs separate workspace management validated without Explorer, full agreed taskbar functionality, and deployment on a confirmed Windows edition. The demo's Explorer-dependent AppBar is not that implementation.

[Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/) supports Enterprise, Education, and IoT Enterprise editions. It replaces the managed account's shell but does not itself stop users accessing other applications. Use standard-user accounts, an enforced application allowlist, and scoped Windows policies, plus a separate administrator recovery account and tested rollback. Administrators and physical device access are outside any absolute no-bypass guarantee.
