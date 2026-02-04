# Iteration 3: Manifest Generator + Rebuild Pipeline (vanilla)

Delivered:

- Build entity and tracking:
  - `Build` table with status, manifest key, file count, size, error message.
- Profile metadata for latest published build:
  - `LatestBuildId`, `LatestManifestKey`, `LatestClientVersion`.
- Rebuild pipeline:
  - reads files from `build-sources/<profile-slug>/...`
  - uploads client files to S3 keys: `clients/<profileSlug>/<buildId>/<relativePath>`
  - generates manifest JSON
  - publishes:
    - `manifests/<profileSlug>/<buildId>.json`
    - `manifests/<profileSlug>/latest.json`
- Admin API:
  - `POST /api/admin/profiles/{id}/rebuild`
  - `GET /api/admin/profiles/{id}/builds`
- Public API:
  - `GET /api/public/manifest/{profileSlug}`
- Admin UI:
  - profile list contains `Rebuild` action and latest build display.

Notes:

- Current rebuild pipeline is vanilla-oriented and file-based.
- Modloader-specific assembly logic will be added in later iterations.
