# Iteration 21: News Retention Policy (Settings + Run)

Delivered:

- Added persistent news retention settings in backend DB (`NewsRetentionConfig`):
  - `enabled`
  - `maxItems`
  - `maxAgeDays`
  - `lastAppliedAtUtc`
  - `lastDeletedItems`
  - `lastError`
- Added admin API:
  - `GET /api/admin/settings/news-retention`
  - `PUT /api/admin/settings/news-retention`
  - `POST /api/admin/settings/news-retention/run`
- Added `NewsRetentionService`:
  - removes old non-pinned items by age limit
  - enforces max non-pinned item count
  - stores last run status/error
- Integrated retention into import pipeline:
  - after each news source sync, retention is applied automatically
- Added fallback options/env:
  - `NEWS_RETENTION_ENABLED`
  - `NEWS_RETENTION_MAX_ITEMS`
  - `NEWS_RETENTION_MAX_AGE_DAYS`
- Added admin UI section:
  - configure retention limits
  - run retention immediately
  - view last apply status
- Added migration `AddNewsRetentionConfig`.
