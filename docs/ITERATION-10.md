# Iteration 10: News Admin CRUD + Launcher News Feed

Delivered:

- Added admin API for managing news:
  - `GET /api/admin/news`
  - `GET /api/admin/news/{id}`
  - `POST /api/admin/news`
  - `PUT /api/admin/news/{id}`
  - `DELETE /api/admin/news/{id}`
- Admin React dashboard now includes:
  - create/edit form for news items (`title/body/source/pinned/enabled`)
  - news list with edit/delete actions
- Launcher now renders latest news from bootstrap in a dedicated panel.
- Added API request examples for news in `BivLauncher.Api.http`.
