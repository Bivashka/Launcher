# Iteration 45: Runtime Artifact Upload Flow (Profile-scoped S3 Keys)

Delivered:

- Extended admin upload backend for runtime artifacts:
  - category `runtimes` now enforces `entityId` (profile slug)
  - runtime object keys are stored under:
    - `runtimes/<profileSlug>/<timestamp>_<guid>.<ext>`
- Increased upload size handling for runtime category:
  - runtime uploads up to `1024 MB`
  - other categories remain `15 MB`
- Added safer key generation behavior:
  - icon path logic is now applied only for `profiles` / `servers`
  - non-icon categories use dedicated upload prefixes
  - entity segment is sanitized for S3 key safety
- Added runtime upload controls in admin UI:
  - profile slug selector
  - file picker for runtime artifacts (`zip/7z/exe/msi/...`)
  - upload action to `/api/admin/upload?category=runtimes&entityId=<profileSlug>`
  - success notice includes resulting S3 key

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
