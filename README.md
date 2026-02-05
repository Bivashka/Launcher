# BivLauncher Platform

Current state: iteration #64 (admin auth + profiles/servers CRUD + rebuild pipeline + player auth + skins/capes + Discord RPC + news CRUD + news sources settings/sync + per-source sync from UI + news auto-sync schedule + manual run-now trigger + news retention policy + retention dry-run + bans CRUD + auth provider + branding settings + S3 settings + launcher MVP + launcher i18n + per-profile route selection RU/DE + launcher server icons from bootstrap + masked password input in launcher login form + full selected news text panel in launcher + launcher self-update check in bootstrap with download CTA + real Discord Rich Presence updates from launcher runtime + HWID HMAC flow + account HWID reset endpoint + launcher full self-update flow with download/install/restart + bundled Java runtime path pipeline per profile + loader-aware build source merge for vanilla/forge/fabric/quilt/neoforge/liteloader + manifest launch strategy metadata: jar/mainclass + classpath + advanced installer.sh with CLI flags, secret auto-generation, strict smoke checks, dry-run mode, JSON report output, CI-friendly exit/error reporting, configurable health/public-IP behavior, custom env-file support, atomic env writes, port availability pre-checks, compose config preflight, env backup safeguards, rollback-on-fail controls, runtime artifact binding pipeline per profile with runtime metadata tracking, runtime artifact integrity metadata in manifest with launcher-side verification, S3 head/metadata support for runtime override manifests, runtime metadata visibility in admin profile form, admin runtime artifact verify endpoint/UI flow, runtime retention cleanup endpoint/UI controls, scheduled runtime retention policy, global runtime retention dry-run filters + JSON export + apply-from-dry-run workflow, safety confirmation dialog before apply, expanded admin audit logs for runtime + CRUD + settings + sync + auth events, audit request metadata (requestId/remoteIp/userAgent), audit time-range filtering (fromUtc/toUtc), runtime audit log filtering by actor/entityType/entityId/requestId/remoteIp, audit export (JSON/CSV), audit cleanup endpoint (dry-run/apply), audit log pagination via offset + load-more, and audit feed sorting/preset filters).

## Stack

- Backend: ASP.NET Core Web API (.NET 8), EF Core, PostgreSQL
- Admin: React + Vite + TypeScript
- Launcher (planned): C# .NET 8 + Avalonia
- Storage (optional): MinIO S3

## Quick start

1. Copy env file:
   ```bash
   cp .env.example .env
   ```
2. Start services:
   ```bash
   docker compose up -d --build
   ```
3. Open:
   - Admin: `http://localhost:5173`
   - API health: `http://localhost:8080/health`

Note: API now reads Postgres connection from `DB_CONN` (in `.env`) as the primary source.

## Installer

- Run guided install:
  - `bash install.sh`
  - (legacy path still works: `bash deploy/installer.sh`)
- Script asks minimal values (ports/host/ssl/minio), updates `.env`, then starts docker stack.
- Non-interactive mode:
  - `BLP_INSTALLER_NON_INTERACTIVE=1 bash install.sh`
- CLI flags:
  - `bash install.sh --non-interactive --host example.com --api-port 8080 --admin-port 5173 --ssl true --with-minio`
  - add `--skip-health-check` to skip HTTP checks after startup
  - add `--strict-check` to fail installer if smoke checks detect issues
  - add `--dry-run` to preview `.env` changes and compose command without applying
  - add `--no-public-ip` to disable external public IP lookup
  - add `--health-retries`, `--health-timeout`, `--health-delay` to tune smoke-check behavior
  - add `--skip-port-check` to bypass local port conflict pre-check
  - add `--skip-compose-config-check` to bypass `docker compose config -q` preflight
  - add `--no-env-backup` to disable env backup creation before modifications
  - add `--rollback-env-on-fail` to restore env from backup if installer fails after writes
  - add `--env-file <path>` to target custom env files
  - add `--output-json installer-report.json` (or `--output-json -`) for machine-readable result
  - add `--setup-admin --admin-user admin --admin-password 'StrongPassword123!'` to auto-create first admin account
  - in non-interactive mode, password can be passed via `BLP_ADMIN_PASSWORD`
  - add `--skip-admin-setup` to disable interactive first-admin prompt
- CI behavior:
  - installer now uses dedicated non-zero exit codes (e.g. `20` docker missing, `21` compose missing, `22` port-in-use, `23` compose-config-invalid, `24` env-backup-failed, `25` compose-up-failed, `26` env-atomic-write-failed, `30` strict-check failure)
  - JSON report includes `result` and `checkSummary` blocks for machine parsing
- Installer auto-generates strong `JWT_SECRET` and `HWID_HMAC_SALT` if they are empty/default placeholders.
- Env updates are written atomically via temp file + rename.
- For existing env files installer creates timestamped backup before first write (unless `--no-env-backup`).
- Compose operations (`up`, `ps`, `config`) respect selected `--env-file`.

## First run

1. Open admin UI.
2. Create first admin account in setup form.
3. Log in with created credentials.
4. Verify bootstrap endpoint:
   - `GET /api/public/bootstrap`

## Implemented admin APIs

- `POST /api/admin/setup`
- `POST /api/admin/login`
- `GET/DELETE /api/admin/audit-logs`
- `GET /api/admin/audit-logs/export`
- `GET/POST/PUT/DELETE /api/admin/profiles`
- `GET/POST/PUT/DELETE /api/admin/servers`
- `POST /api/admin/upload`
- `GET /api/admin/runtimes/verify`
- `POST /api/admin/runtimes/cleanup`
- `POST /api/admin/profiles/{id}/rebuild`
- `GET /api/admin/profiles/{id}/builds`
- `GET /api/public/manifest/{profileSlug}`
- `POST /api/public/auth/login`
- `GET /api/public/skins/{user}`
- `GET /api/public/capes/{user}`
- `POST /api/admin/skins/{user}/upload`
- `POST /api/admin/capes/{user}/upload`
- `GET/PUT/DELETE /api/admin/discord-rpc/profile/{profileId}`
- `GET/PUT/DELETE /api/admin/discord-rpc/server/{serverId}`
- `GET/PUT /api/admin/settings/discord-rpc`
- `GET/POST /api/admin/news`
- `GET/PUT/DELETE /api/admin/news/{id}`
- `GET /api/admin/bans`
- `POST /api/admin/bans/hwid`
- `POST /api/admin/bans/account/{user}`
- `POST /api/admin/bans/account/{user}/reset-hwid`
- `DELETE /api/admin/bans/{id}`
- `GET /api/admin/crashes`
- `PUT /api/admin/crashes/{id}/status`
- `GET /api/admin/crashes/export`
- `GET/POST /api/admin/docs`
- `GET/PUT/DELETE /api/admin/docs/{id}`
- `POST /api/admin/docs/seed`
- `GET/PUT /api/admin/settings/auth-provider`
- `POST /api/admin/settings/auth-provider/probe`
- `GET/POST/DELETE /api/admin/wizard/preflight-runs`
- `GET/PUT /api/admin/settings/two-factor`
- `GET /api/admin/settings/two-factor/accounts`
- `PUT /api/admin/settings/two-factor/accounts/{id}`
- `POST /api/admin/settings/two-factor/accounts/{id}/enroll`
- `POST /api/admin/settings/two-factor/accounts/{id}/reset`
- `GET/PUT /api/admin/settings/branding`
- `GET /api/admin/support/developer`
- `GET/PUT /api/admin/settings/install-telemetry`
- `GET /api/admin/install-telemetry/projects`
- `GET/PUT /api/admin/settings/s3`
- `POST /api/admin/settings/s3/test`
- `POST /api/admin/settings/s3/migrate`
- `GET/PUT /api/admin/settings/news-sources`
- `POST /api/admin/settings/news-sources/sync`
- `GET/PUT /api/admin/settings/news-sync`
- `POST /api/admin/settings/news-sync/run`
- `GET/PUT /api/admin/settings/news-retention`
- `POST /api/admin/settings/news-retention/run`
- `POST /api/admin/settings/news-retention/dry-run`
- `GET/PUT /api/admin/settings/runtime-retention`
- `POST /api/admin/settings/runtime-retention/run`
- `POST /api/admin/settings/runtime-retention/dry-run`
- `POST /api/admin/settings/runtime-retention/run-from-preview`
- `POST /api/public/crashes`
- `POST /api/public/install-telemetry`
- `GET /api/public/docs`
- `GET /api/public/docs/{slug}`

## Launcher MVP

- Avalonia launcher project: `launcher/BivLauncher.Client`
- Build:
  - `dotnet build launcher/BivLauncher.Launcher.sln`
- Run:
  - `dotnet run --project launcher/BivLauncher.Client/BivLauncher.Client.csproj`

## Tests

- Backend tests:
  - `dotnet test backend/BivLauncher.sln`
  - includes rate-limit response/partition-key checks (`backend/BivLauncher.Api.Tests`)
- End-to-end smoke checks (build + tests):
  - Windows: `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -SkipApi`
  - Linux/macOS: `bash scripts/smoke.sh`
  - optional API health check: pass base URL (`powershell ... -ApiBaseUrl http://localhost:8080` or `bash scripts/smoke.sh http://localhost:8080`)
- CI:
  - GitHub Actions workflow: `.github/workflows/ci.yml`
  - runs `npm ci` (admin) + `bash scripts/smoke.sh` on push/PR

## Delivery Blueprint

- `Architecture`: launcher (Avalonia) consumes public API (`bootstrap`, `manifest`, `auth`, `news`, `docs`) and uploads crash/telemetry; admin (React) manages all settings/features via protected admin API; backend persists state in PostgreSQL and files in S3/local storage.
- `Core data domains`: profiles/servers/builds, auth providers/accounts/2FA, crashes, docs, news/news-sources/retention, storage settings/migrations, installer telemetry, audit log.
- `Operational flows`: install (`install.sh`) -> setup admin -> configure integrations/storage/auth -> publish profile/build -> launcher bootstrap/login/verify/launch -> monitor crashes/audit/news sync.

## Roadmap (MVP -> v1 -> v1.1)

- `MVP` (done, high): admin + backend + launcher core, auth modes (external/any), profile/server/build pipeline, storage switch, crash ingest, docs/FAQ, basic wizard/preflight, installer one-command flow.
- `v1` (in progress, medium): stronger UX polish, broader localization pass, complete operational runbooks, additional integration tests for critical admin flows.
- `v1.1` (planned, medium/high): richer observability dashboards, optional analytics/reporting expansion, advanced deploy topologies and backup automation presets.

## UX Screen Map

- `Admin`: Overview -> Setup Wizard -> Servers & Profiles -> Build & Runtime -> News -> Integrations -> Security -> Crashes -> Docs -> Branding -> Audit.
- `Launcher`: Login -> News + server/profile selection -> Play/verify -> Post-login settings -> Crash summary/retry path.
- `Design principle`: no required file editing on client side; all runtime/auth/branding/integration settings managed in admin UI.

## Migration and Compatibility

- `DB migrations`: EF migrations in `backend/BivLauncher.Api/Data/Migrations`; apply on startup via `db.Database.Migrate()`.
- `Backward compatibility`: optional frontend calls (e.g. wizard history) are non-blocking to keep admin usable against partially updated backends.
- `Storage migration`: controlled dry-run/apply path via `/api/admin/settings/s3/migrate` with explicit target mode and bounded scan.

## Built-in Documentation/FAQ Skeleton

- `Install`: first setup, env variables, docker/install script modes.
- `Operate`: backup/restore, update sequence, troubleshooting health/storage/auth.
- `Launcher support`: crash triage checklist, common auth/runtime/network issues.
- `Integrations`: Discord RPC, news providers (TG/VK/RSS/JSON), S3/local storage patterns.

## Definition of Done

- `Functional`: all A-O requirements available from admin/launcher without manual code edits for owners.
- `Security/privacy`: no secrets in telemetry/crash payloads, sensitive masking, rate-limits on public/admin auth-sensitive endpoints.
- `Reliability`: offline queue for launcher submissions, retry logic, preflight probes, install smoke checks.
- `Quality gates`: `dotnet test backend/BivLauncher.sln`, `dotnet build launcher/BivLauncher.Launcher.sln`, `npm run build` for admin.
- `Release process`: use `RELEASE_CHECKLIST.md` before production rollout.


