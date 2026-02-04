# Iteration 50: Runtime Retention Cleanup (API + Admin UI)

Delivered:

- Extended storage abstraction with runtime cleanup primitives:
  - `ListByPrefixAsync(prefix)`
  - `DeleteAsync(key)`
- Implemented S3-backed listing and deletion in object storage service.
- Added admin runtime cleanup endpoint:
  - `POST /api/admin/runtimes/cleanup?entityId=<profileSlug>&keepLast=<N>&dryRun=<bool>`
  - keeps current profile runtime key and latest `N` runtime artifacts under `runtimes/<profileSlug>/`
  - supports dry-run preview and real deletion mode
- Added admin UI controls in runtime section:
  - keep-last input
  - dry-run toggle
  - cleanup action button
  - result summary (found/keep/delete/deleted)
- Updated API HTTP collection with runtime cleanup examples.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
