# Iteration 64: Audit Finalization (Export + Cleanup + Indexes)

Delivered:

- Extended audit API with export endpoint:
  - `GET /api/admin/audit-logs/export`
  - supports current filter set (`actionPrefix`, `actor`, `entityType`, `entityId`, `requestId`, `remoteIp`, `fromUtc`, `toUtc`, `sort`)
  - supports `format=json|csv` and `limit` (up to 50000)
- Added audit cleanup endpoint:
  - `DELETE /api/admin/audit-logs?olderThanUtc=<utc>&dryRun=true|false&limit=<n>`
  - dry-run preview and apply modes
  - returns eligible/candidate/deleted stats
- Added self-auditing events for audit operations:
  - `audit.export`
  - `audit.cleanup`
- Added DB indexes for audit-heavy filters:
  - `(Action, CreatedAtUtc)`
  - `(Actor, CreatedAtUtc)`
  - `(RemoteIp, CreatedAtUtc)`
- Admin UI audit panel improvements:
  - added `entityType` exact filter
  - date range presets (`24h`, `7d`, `30d`, `90d`, `all`)
  - clear filters action
  - export buttons (JSON/CSV) with configurable export limit
  - cleanup controls (older-than days, batch limit, dry-run/apply)
  - default action filter switched to all actions (not runtime-only)
- Updated API HTTP examples for export + cleanup.

Verification:

- `dotnet ef migrations add AddAdminAuditQueryIndexes`
- `dotnet build backend/BivLauncher.sln`
- `npm run build` (admin)
