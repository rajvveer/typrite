# Typrite

Windows 11 desktop bar that reserves a strip of workspace (clock, weather, ad region) via the Win32 AppBar API, yields to full-screen apps, and reports visibility honestly.

## Layout

| Project | Purpose | Builds on |
|---|---|---|
| `src/Typrite.Shell` | Pure logic: DPI/geometry, AppBar rectangle math, fullscreen classification, per-display state machine, stable display keys | any OS |
| `src/Typrite.Shell.Win32` | P/Invoke adapter: `SHAppBarMessage`, HWND subclassing, WinEvent hooks | Windows |
| `src/Typrite.Desktop` | WinUI 3 bar window, one per display | Windows |
| `tests/Typrite.Shell.Tests` | xUnit tests for `Typrite.Shell` | any OS |

## Build

```sh
dotnet test tests/Typrite.Shell.Tests                     # any OS
dotnet build src/Typrite.Shell.Win32                      # any OS (Windows targeting enabled)
dotnet build src/Typrite.Desktop -p:Platform=x64          # Windows only
```

Requires .NET 10 SDK. The Desktop project needs Windows App SDK 2.6.4 and must be smoke-tested on a physical Windows machine; CI compiles it but cannot validate shell behaviour.

## Status

P1 (native bar proof): bar window per display, AppBar registration, re-register on Explorer restart, DPI change handling, clock.
P2 (full-screen controller): WinEvent-hook detection, DWM bounds and cloaking, per-display classification with yield by default, allowlisted overlay with a visibility probe, secure-desktop detection, display rebuild on topology change, stable display keys from EDID.
Weather and ad regions are placeholders. Not yet verified on physical Windows hardware.

## Rules

- Full-screen apps win by default. Overlay only for an explicitly allowlisted app identity (path + signer) that passed a physical test.
- Never claim an impression while the bar is hidden.
- No Win32 outside `Typrite.Shell.Win32`. Logic stays testable.
- One AppBar per display; `ABM_REMOVE` on every exit path.
