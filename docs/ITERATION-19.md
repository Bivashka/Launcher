# Iteration 19: News Auto-sync Schedule (Background Service)

Delivered:

- Added persistent auto-sync settings in backend DB (`NewsSyncConfig`):
  - `enabled`
  - `intervalMinutes`
  - `lastRunAtUtc`
  - `lastRunError`
- Added admin API for auto-sync settings:
  - `GET /api/admin/settings/news-sync`
  - `PUT /api/admin/settings/news-sync`
- Added fallback options (`NewsSyncOptions`) with env overrides:
  - `NEWS_SYNC_ENABLED`
  - `NEWS_SYNC_INTERVAL_MINUTES`
- Added `NewsSyncHostedService`:
  - runs once per minute
  - checks schedule from DB (or fallback options)
  - executes import via `INewsImportService`
  - writes last run timestamp/error to DB
- Added docker/env wiring for news sync fallback variables.
