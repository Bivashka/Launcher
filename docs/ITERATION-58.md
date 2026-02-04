# Iteration 58: Audit Logs Pagination + Load More

Delivered:

- Extended admin audit logs API with pagination:
  - added `offset` query parameter to `GET /api/admin/audit-logs`
  - supports filtered paging with existing `actionPrefix/actor/entityId`
- Admin UI runtime audit panel now supports paged loading:
  - `Refresh logs` resets to offset `0`
  - `Load more` appends next chunk using current filters
  - status line shows loaded count, next offset, and has-more flag
- Added reusable audit query builder and paged fetch helper in admin app.
- Updated API HTTP examples with offset-based audit query.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
