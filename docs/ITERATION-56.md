# Iteration 56: Runtime Audit Logging

Delivered:

- Added persistent admin audit log storage:
  - new entity `AdminAuditLog`
  - new table via EF migration `AddAdminAuditLogs`
  - fields: action, actor, entityType, entityId, detailsJson, createdAtUtc
- Added audit write service:
  - `IAdminAuditService`
  - `AdminAuditService` (safe write with truncation + warning on failure)
- Integrated runtime audit events:
  - manual runtime cleanup (`POST /api/admin/runtimes/cleanup`)
  - apply-from-dry-run (`POST /api/admin/settings/runtime-retention/run-from-preview`)
- Added admin audit feed endpoint:
  - `GET /api/admin/audit-logs?limit=50&actionPrefix=runtime`
- Admin UI updates:
  - runtime audit logs block with latest entries
  - refresh action
  - details preview for runtime operations

Verification:

- `dotnet ef migrations add AddAdminAuditLogs`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
