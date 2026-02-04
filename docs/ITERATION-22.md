# Iteration 22: News Retention Dry-run

Delivered:

- Added backend dry-run endpoint:
  - `POST /api/admin/settings/news-retention/dry-run`
- Added dry-run response model with detailed projection:
  - total items
  - would delete by age
  - would delete by overflow
  - total delete
  - remaining items
- Extended `INewsRetentionService` and `NewsRetentionService`:
  - added `PreviewRetentionAsync()`
  - unified retention plan calculation for preview and apply paths
- Added admin UI action in `News Retention`:
  - `Dry-run` button
  - preview line in status panel
- Added HTTP example for dry-run in `BivLauncher.Api.http`.
