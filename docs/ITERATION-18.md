# Iteration 18: Per-source News Sync from Admin UI

Delivered:

- Added per-source sync action in admin dashboard (`News Sources` section).
- Reused existing backend endpoint:
  - `POST /api/admin/settings/news-sources/sync?sourceId=<guid>`
- Added user feedback after per-source sync:
  - imported items count
  - source-level error message (if any)
- Refreshed admin data after sync to show updated:
  - `lastSyncAtUtc`
  - `lastSyncError`
- Added HTTP example for single-source sync in `BivLauncher.Api.http`.
