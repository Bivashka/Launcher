# Iteration 20: News Auto-sync "Run now" Trigger

Delivered:

- Added admin endpoint for immediate scheduled-sync execution:
  - `POST /api/admin/settings/news-sync/run`
- Endpoint behavior:
  - triggers `INewsImportService.SyncAsync(null)` immediately
  - updates `NewsSyncConfig.LastRunAtUtc` and `LastRunError`
  - returns import summary (`sourcesProcessed`, `imported`, per-source results)
- Added admin UI button in `News Auto-sync` section:
  - `Run now`
  - shows summary message and refreshes settings/status after execution
- Added HTTP example request in `BivLauncher.Api.http`.
