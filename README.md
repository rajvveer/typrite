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
| `src/Typrite.Api` | ASP.NET Core minimal API: enrollment, device, delivery, events, weather, releases, admin | any OS |
| `src/Typrite.Worker` | Outbox-driven daily delivery summaries and partner reconciliation | any OS |
| `src/Typrite.Agent` | Device agent: TPM/CNG device key, enrollment, heartbeat, commands, signed updates, token pipe to the bar | Windows service; console host elsewhere |
| `src/admin` | React + TypeScript operator console (Vite, Playwright smoke tests) | any OS |
| `installer/` | WiX 5 package: agent service, per-user desktop launch, claim token drop | Windows |
| `tools/load/fleet.js` | k6 fleet model from the guide (decisions, heartbeats, event batches) | any OS |
| `tests/*` | xUnit; API and Worker tests need PostgreSQL (`TYPRITE_TEST_PG`) | any OS |

## Build

```sh
dotnet test                                               # all tests; API and Worker tests need PostgreSQL
dotnet run --project src/Typrite.Api                      # https://localhost:7210, migrates and seeds in Development
dotnet run --project src/Typrite.Worker                   # aggregation + reconciliation
dotnet run --project src/Typrite.Agent                    # console host; needs Agent:ClaimToken or claim.token
cd src/admin && pnpm install && pnpm dev                  # console at http://localhost:5173, proxies /api
docker compose up                                         # PostgreSQL + API + worker
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
P5 (admin + partner): operator console, mock partner adapter behind `IAdPartner`, worker that rolls raw events into daily summaries and reconciles against the partner report with a tolerance.
P6 (fleet): one-time enrollment claims, per-device key in CNG (TPM first), proof-of-possession device tokens, agent heartbeat/command loop, cohort-gated signed release manifests verified against a pinned publisher key, diagnostics upload, named-pipe token brokering to the bar.
P7 (release tooling): WiX package definition, k6 fleet script, compose stack, CI for every test project plus the console.

Unverified: nothing has run on physical Windows hardware. The WinUI desktop, the agent's CNG and pipe-ACL paths, and the installer compile or run only on Windows. Billability is undefined until a real partner contract exists; every event is technical evidence only.

## Rules

- Full-screen apps win by default. Overlay only for an explicitly allowlisted app identity (path + signer) that passed a physical test.
- Never claim an impression while the bar is hidden.
- No Win32 outside `Typrite.Shell.Win32`. Logic stays testable.
- One AppBar per display; `ABM_REMOVE` on every exit path.
