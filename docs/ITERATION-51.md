# Iteration 51: Scheduled Runtime Retention Policy

Delivered:

- Added runtime retention configuration model and migration:
  - `RuntimeRetentionConfig` table
  - fields: `enabled`, `intervalMinutes`, `keepLast`, `lastRunAtUtc`, `lastDeletedItems`, `lastProfilesProcessed`, `lastRunError`
- Added runtime retention options with env/appsettings fallback:
  - `RUNTIME_RETENTION_ENABLED`
  - `RUNTIME_RETENTION_INTERVAL_MINUTES`
  - `RUNTIME_RETENTION_KEEP_LAST`
- Added runtime retention service + hosted scheduler:
  - background check every minute
  - respects enabled + interval
  - applies cleanup across all profiles under `runtimes/<profileSlug>/`
  - keeps profile active runtime key and latest `N` runtime artifacts
- Added admin settings API:
  - `GET /api/admin/settings/runtime-retention`
  - `PUT /api/admin/settings/runtime-retention`
  - `POST /api/admin/settings/runtime-retention/run`
- Admin UI updates:
  - runtime retention settings controls (enabled/interval/keep-last)
  - run-now button
  - runtime retention status display (last run, deleted count, profiles processed, last error)

Verification:

- `dotnet ef migrations add AddRuntimeRetentionConfig`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
