# Iteration 25: Full News Reader Panel in Launcher

Delivered:

- Added selected-news state in launcher view model (`SelectedNewsItem`).
- Extended launcher news item model with:
  - `Id` (stable selection restore after relocalization)
  - `Body` (full news content)
- Updated bootstrap-to-UI mapping to preserve full news body, not only preview.
- Added full news reader area in launcher UI:
  - news list keeps short preview/meta
  - selected item title and full body are shown in a read-only text panel
- Preserved selected news item while relocalizing/rebuilding collections.
- Verified launcher solution builds cleanly.
