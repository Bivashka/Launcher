# Iteration 30: Bundled Java Runtime Path Pipeline per Profile

Delivered:

- Added profile-level bundled Java runtime field:
  - DB entity: `Profile.BundledJavaPath`
  - migration: `AddProfileBundledJavaPath`
- Extended profile admin contract/API:
  - `ProfileDto.bundledJavaPath`
  - `ProfileUpsertRequest.bundledJavaPath`
  - create/update/list/get endpoints now persist and return this field
- Extended build pipeline:
  - `ProfileRebuildRequest.javaRuntimePath` (optional override)
  - manifest `javaRuntime` now resolved from:
    1) rebuild request override (if provided)
    2) profile `bundledJavaPath`
  - added validation for runtime path:
    - must be relative
    - no path traversal (`..`)
- Updated admin dashboard UI:
  - profile form includes `Bundled Java path (relative, optional)`
  - profile list shows configured bundled Java path
- Updated build-sources doc with bundled Java path usage guidance.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
