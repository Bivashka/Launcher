# Iteration 62: Audit Request Metadata + Filters

Delivered:

- Extended admin audit log data model with request metadata:
  - `RequestId` (trace id / `X-Request-Id`)
  - `RemoteIp` (`X-Forwarded-For` first hop or connection IP)
  - `UserAgent`
- `AdminAuditService` now auto-populates request metadata for all audit writes.
- Added API support for audit filtering by request metadata:
  - `GET /api/admin/audit-logs` now supports:
    - `requestId` (exact)
    - `remoteIp` (exact)
- Extended audit DTO/response with:
  - `requestId`
  - `remoteIp`
  - `userAgent`
- Admin UI audit section updates:
  - added filter inputs for `requestId` and `remoteIp`
  - added request metadata lines in feed item rendering (`req`, `ip`, `ua`)
- Updated HTTP examples with request-metadata filter query.

Verification:

- `dotnet ef migrations add AddAdminAuditRequestMetadata`
- `dotnet build backend/BivLauncher.sln`
- `npm run build` (admin)
