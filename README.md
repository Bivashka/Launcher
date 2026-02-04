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

## Installer

- Run guided install:
  - `bash deploy/installer.sh`
- Script asks minimal values (ports/host/ssl/minio), updates `.env`, then starts docker stack.
- Non-interactive mode:
  - `BLP_INSTALLER_NON_INTERACTIVE=1 bash deploy/installer.sh`
- CLI flags:
  - `bash deploy/installer.sh --non-interactive --host example.com --api-port 8080 --admin-port 5173 --ssl true --with-minio`
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
- `GET/POST /api/admin/news`
- `GET/PUT/DELETE /api/admin/news/{id}`
- `GET /api/admin/bans`
- `POST /api/admin/bans/hwid`
- `POST /api/admin/bans/account/{user}`
- `POST /api/admin/bans/account/{user}/reset-hwid`
- `DELETE /api/admin/bans/{id}`
- `GET/PUT /api/admin/settings/auth-provider`
- `GET/PUT /api/admin/settings/branding`
- `GET/PUT /api/admin/settings/s3`
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

## Launcher MVP

- Avalonia launcher project: `launcher/BivLauncher.Client`
- Build:
  - `dotnet build launcher/BivLauncher.Launcher.sln`
- Run:
  - `dotnet run --project launcher/BivLauncher.Client/BivLauncher.Client.csproj`


