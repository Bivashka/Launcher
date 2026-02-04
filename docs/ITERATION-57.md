# Iteration 57: Runtime Audit Log Filters (API + Admin UI)

Delivered:

- Extended admin audit logs API filtering:
  - `GET /api/admin/audit-logs` now supports:
    - `actionPrefix` (existing)
    - `actor` (exact match)
    - `entityId` (contains match)
    - `limit` (1..500)
- Admin panel runtime audit section now has filter controls:
  - action prefix
  - actor
  - entity id contains
  - limit
  - manual refresh with current filter set
- Runtime audit feed remains focused on runtime operations and displays latest filtered entries.
- Updated HTTP examples with filtered audit query.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
