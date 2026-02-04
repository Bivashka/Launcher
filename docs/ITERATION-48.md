# Iteration 48: Runtime Metadata via S3 Head + Override Support + Admin Visibility

Delivered:

- Extended object storage abstraction with metadata/head support:
  - `IObjectStorageService.GetMetadataAsync(key)`
  - `StoredObjectMetadata(sizeBytes, contentType, sha256)`
- `S3ObjectStorageService` now:
  - supports optional metadata on upload
  - stores custom runtime `sha256` metadata on runtime upload
  - resolves object metadata through `GetObjectMetadata` (HEAD-like request)
- Runtime upload flow now writes runtime sha256 into object metadata and still persists profile runtime metadata in DB.
- Build pipeline runtime metadata resolution improved:
  - keeps profile metadata when rebuild runtime key matches profile runtime key
  - falls back to S3 object metadata for override runtime keys
  - manifest runtime integrity fields are now populated for overrides when metadata exists in object storage
- Admin UI improvements:
  - profile form now shows runtime metadata (size/content-type/sha256) for edited profile/slug
  - profile list runtime metadata now uses human-readable byte units

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
