# Iteration 49: Runtime Artifact Verify API + Admin Verify UI

Delivered:

- Added runtime verify endpoint for admins:
  - `GET /api/admin/runtimes/verify`
  - supports verification by profile slug (`entityId`) and/or direct runtime key (`key`)
  - returns object-storage metadata (size/content-type/sha256) and comparison flags against profile metadata
- Added runtime key normalization and validation in verify flow.
- Extended runtime upload flow continuity:
  - runtime upload still writes sha256 metadata into object storage and stores profile runtime metadata in DB.
- Admin panel runtime section improved:
  - added runtime key override input for verification
  - added `Verify runtime artifact` action
  - shows verification snapshot and match flags (hash/size/content-type)
- Added request examples for runtime verification to API HTTP collection.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
