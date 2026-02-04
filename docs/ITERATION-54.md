# Iteration 54: Apply-from-Dry-Run + Copy Keys Workflow

Delivered:

- Extended runtime retention dry-run API payload with workflow metadata:
  - `profileSlugFilter`, `maxProfiles`, `previewKeysLimit`
  - `profilesReturned`, `hasMoreProfiles`
- Added apply endpoint for dry-run workflow:
  - `POST /api/admin/settings/runtime-retention/run-from-preview`
  - applies deletions using the same filter scope (`profileSlug`, `maxProfiles`)
- Runtime retention service now supports:
  - `ApplyRetentionFromPreviewAsync(...)`
  - shared planning batch logic for dry-run + apply-from-preview
- Admin UI runtime retention section now includes:
  - `Apply from dry-run` action
  - `Copy delete keys` action (clipboard)
  - dry-run summary aligned with returned/hidden profiles metadata
  - displays all returned preview profiles (up to selected max)
- Updated HTTP examples and README API list/state.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
