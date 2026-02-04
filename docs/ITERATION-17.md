# Iteration 17: News Sources Settings + Manual Sync

Delivered:

- Added persistent news source settings in backend DB (`NewsSourceConfig`):
  - `name`
  - `type` (`rss`, `json`, `markdown`)
  - `url`
  - `enabled`
  - `maxItems`
  - `lastSyncAtUtc`
  - `lastSyncError`
- Added admin API for news source settings:
  - `GET /api/admin/settings/news-sources`
  - `PUT /api/admin/settings/news-sources` (replace full source list)
- Added sync API:
  - `POST /api/admin/settings/news-sources/sync`
  - optional query `sourceId` for syncing one source
- Implemented `NewsImportService`:
  - fetches remote content via `HttpClient`
  - parses RSS / JSON / Markdown
  - imports deduplicated news items into `NewsItems`
  - updates per-source sync status/error
- Added admin dashboard section for:
  - editing source list (local form + save)
  - manual sync trigger
  - viewing source sync status/errors
- Added migration `AddNewsSourceConfig`.
