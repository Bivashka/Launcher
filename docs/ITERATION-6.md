# Iteration 6: Skins/Capes Service + Launcher Integration

Delivered:

- New DB entities:
  - `SkinAsset` (`accountId`, `key`, `updatedAtUtc`)
  - `CapeAsset` (`accountId`, `key`, `updatedAtUtc`)
- Public endpoints:
  - `GET /api/public/skins/{user}`
  - `GET /api/public/capes/{user}`
  - `GET /api/public/skins/{user}/meta`
  - `GET /api/public/capes/{user}/meta`
- Admin endpoints:
  - `POST /api/admin/skins/{user}/upload`
  - `POST /api/admin/capes/{user}/upload`
- Launcher integration:
  - after login, launcher checks skin/cape availability
  - account panel now shows `Skin: true/false`, `Cape: true/false`

Notes:

- `user` supports `username` or `externalId`.
- Files are stored in S3 using keys like:
  - `skins/{accountId}/skin.<ext>`
  - `capes/{accountId}/cape.<ext>`
