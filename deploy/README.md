# Hosted MVP with Neon

The API image builds and serves the React dashboard at `/`; all browser API requests
use the same origin. Run one API instance with a persistent blob volume and one worker.
Neon stores PostgreSQL data, not creative files. This is a single-host demo deployment,
not a horizontally scalable storage or authentication deployment.

## Required configuration

Copy `deploy/hosted.env.example` to `.env.hosted` and fill it using your host's secret
manager or a private local file. Never put database passwords, signing keys or client
secrets in React/Vite configuration. No SPA client secret is needed.

- Use Neon's .NET/Npgsql connection string (`Host=...;Database=...;Username=...;Password=...`),
  with `SSL Mode=VerifyFull`. Use the pooler hostname for normal API/worker traffic and
  the direct hostname for migrations. Start with a bounded connection pool per process
  and size it for the chosen Neon plan. Preserve certificate verification.
- Retain `DEVICE_SIGNING_KEY` across restarts and deployments. Generate it from at least
  32 random bytes; do not use the example text. Rotating it invalidates existing device tokens.
- Retain the RSA release signing private key and provision its matching public key in
  each agent. Store the private key only on the API host. Do not generate new keys on
  every container start.
- `PUBLIC_API_URL` is the public HTTPS origin, e.g. `https://demo.example.com`.
- Mount the `blobs` volume at `/data/blobs`, writable by the container's non-root app user.
  Back it up with the database; never use `docker compose down -v` to update this deployment.
  An absolute storage path does not prove that the hosting provider persists it.

Neon explains [TLS connection protection](https://neon.com/blog/postgres-needs-better-connection-security-defaults).
Use a dedicated demo database/branch and credentials; the browser must never connect to it directly.

## Operator sign-in

Register a public browser client in an OIDC provider supporting authorization code with
PKCE and browser token exchange. Configure the exact callback URL `https://demo.example.com/`
and permitted web origin. Typrite requests the configured `OIDC_SCOPES`; include the API's
required scope and configure the provider to issue an access token for `OIDC_AUDIENCE`.
Provider-specific audience/resource settings may be required in the provider registration.

Set `OIDC_AUTHORITY` to the provider's issuer/discovery authority and `OIDC_CLIENT_ID`
to the browser client ID. Require MFA and authorize named operators through that provider.
The API expects **access-token** claims:

- `sub`: stable operator subject;
- `role`: one of `Viewer`, `CampaignOperator`, `DeviceOperator`, `ReleaseManager`, `Administrator`;
- `account_id`: the authorized account GUID, assigned by administrators, never self-editable profile data.

The seeded account is `00000000-0000-0000-0000-000000000001`. Assign a demo operator to
that account explicitly. Configure claim mappings at the provider; claims only present
in the ID token will not authorize API calls. Login fails closed if the provider or
required claims are missing. Tokens expire normally; users sign in again when needed.
The dashboard's Sign out clears its local session; it does not sign out other applications
at the identity provider.

Development login and device registration remain restricted to local Development/Testing.
Production startup requires stable signing keys, OIDC settings, a public HTTPS URL,
an absolute storage path, and a certificate-verified PostgreSQL connection. OpenAPI is
not publicly exposed in production.

## Build, migrate, run

Run on a Docker host from the repository root:

```sh
docker compose --env-file .env.hosted -f compose.hosted.yaml build
```

Before first startup or an upgrade, run migrations once against a **direct** Neon
connection. Use a separate private `.env.migrate` with the same settings and the direct
database hostname. Take a database backup/branch before upgrading a populated database.

```sh
docker compose --env-file .env.migrate -f compose.hosted.yaml run --rm api --migrate-only
docker compose --env-file .env.hosted -f compose.hosted.yaml up -d
```

`--migrate-only` applies EF migrations and baseline seed rows, then exits without serving
requests. Normal hosted startup does not migrate. Deploy a compatible worker after the
migration; when rolling back, retain schema compatibility rather than blindly downgrading data.

The API listens on host loopback port 8080. Put an HTTPS reverse proxy in front of it and
route the public origin to that port. Do not publish the backend HTTP port to the internet.
Configure `Proxy__KnownProxies__0` (and additional numbered entries) with the exact proxy
addresses seen by Kestrel, using a Compose override or the hosting platform's environment
settings. The edge must overwrite forwarded headers and rate-limit anonymous endpoints.
Never enable blanket forwarded-header trust; IP-based enrollment limits otherwise become
spoofable or all devices can share one proxy bucket. See Microsoft's
[proxy configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

For a platform that deploys images directly, use the API and worker Dockerfiles with the
same environment settings and a persistent API disk. There is no need for a separate
static frontend host or permissive CORS policy.

## Windows device connection

Point `typrite.json` and the agent's `Agent:ApiBaseUrl` at the public HTTPS origin. Public
production deployments require agent enrollment; development device credentials do not work.
Use an authorized operator to create a one-time claim (`POST /api/v1/admin/enrollment-claims`),
then supply the raw claim to the agent through its protected provisioning path. Verify the
agent is enrolled and the desktop obtains its identity/token through the local pipe before
demonstrating delivery. The current MSI provisioning issues are tracked in
[managed fleet requirements](../docs/managed-fleet/README.md); hosting does not fix them.

## Acceptance checks before sharing the demo

1. `/health` returns success, the dashboard loads, and a real OIDC login reaches Devices.
2. Anonymous admin requests return 401; a Viewer cannot create campaigns; cross-account
   requests are denied. Development authentication endpoints return 404.
3. Enroll a Windows device, upload/activate a creative, and verify its HTTPS download and
   delivery events. Confirm worker-generated summaries appear.
4. Restart API and worker. Tokens/identity remain usable and uploaded creatives remain
   available. Backups and the administrative recovery route work.
5. Confirm the browser only contacts the public API/identity provider; no Neon password,
   private signing key or privileged credential appears in frontend assets.

The application tests do not substitute for a live Neon/provider/Windows acceptance run.
