# Typrite

Windows 11 desktop bar that reserves a strip of workspace (clock, weather, ad region) via the Win32 AppBar API, yields to full-screen apps, and reports visibility honestly.

## Layout

| Project | Purpose | Builds on |
|---|---|---|
| `src/Typrite.Shell` | Pure logic: DPI/geometry, AppBar rectangle math, fullscreen classification, per-display state machine, stable display keys | any OS |
| `src/Typrite.Shell.Win32` | P/Invoke adapter: `SHAppBarMessage`, HWND subclassing, WinEvent hooks | Windows |
| `src/Typrite.Desktop` | WinUI 3 bar window, one per display; ad region, clock, weather | Windows |
| `src/Typrite.Contracts` | Wire DTOs shared by device, agent, API and worker | any OS |
| `src/Typrite.Player` | Creative validation, bounded cache, visibility tracker, delivery pipeline, outbox uploader | any OS |
| `src/Typrite.LocalStore` | SQLite event outbox and on-disk creative store | any OS |
| `src/Typrite.Domain` | Entities and rules: ad selection, campaign lifecycle, event ingest, rollout cohorts | any OS |
| `src/Typrite.Infrastructure` | EF Core (PostgreSQL), blob storage, weather providers, partner adapters | any OS |
| `src/Typrite.Api` | ASP.NET Core minimal API: device, delivery, events, weather, admin | any OS |
| `tests/*` | xUnit; `Typrite.Api.IntegrationTests` needs PostgreSQL (`TYPRITE_TEST_PG`) | any OS |

## Build

```sh
dotnet test                                               # all logic tests; API tests need PostgreSQL
dotnet run --project src/Typrite.Api                      # https://localhost:7210, migrates and seeds in Development
dotnet build src/Typrite.Desktop -p:Platform=x64          # Windows only
```

API tests expect a reachable PostgreSQL. Default `Host=localhost;Database=typrite_test;Username=thenoah`; override with `TYPRITE_TEST_PG`.

Development auth: `POST /api/v1/devices/dev-register` and `POST /api/v1/admin/dev-login` exist only while `Auth:AllowDevAuth` is true. Production operators come from the OIDC authority in `Auth:OperatorAuthority`.

Requires .NET 10 SDK. The Desktop project needs Windows App SDK 2.6.4 and must be smoke-tested on a physical Windows machine; CI compiles it but cannot validate shell behaviour.

## Status

P1 (native bar proof): bar window per display, AppBar registration, re-register on Explorer restart, DPI change handling, clock.
P2 (full-screen controller): WinEvent-hook detection, DWM bounds and cloaking, per-display classification with yield by default, allowlisted overlay with a visibility probe, secure-desktop detection, display rebuild on topology change, stable display keys from EDID.
P3 (ad player + offline): manifest and byte validation, LRU creative cache, visibility tracker that never claims visibility while hidden, SQLite outbox with drop counters, batched upload with backoff, house fallback.
P4 (backend MVP): device registration, ETag configuration, idempotent ad decisions scoped to the device's account, creative content, idempotent event ingest with outbox, weather proxy with per-cell cache, commands, campaign lifecycle with audit, releases, RBAC and account tenancy.
Not yet verified on physical Windows hardware.

## Rules

- Full-screen apps win by default. Overlay only for an explicitly allowlisted app identity (path + signer) that passed a physical test.
- Never claim an impression while the bar is hidden.
- No Win32 outside `Typrite.Shell.Win32`. Logic stays testable.
- One AppBar per display; `ABM_REMOVE` on every exit path.
