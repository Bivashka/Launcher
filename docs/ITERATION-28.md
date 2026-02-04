# Iteration 28: HWID HMAC Flow + Account HWID Reset

Delivered:

- Hardened HWID processing in backend auth flow:
  - launcher now sends `hwidFingerprint` (normalized, SHA-256 fingerprint token)
  - backend computes final `hwidHash` as `HMAC-SHA256(hwidFingerprint, HWID_HMAC_SALT)`
  - stored/checked ban values remain hashed only (`HardwareBans.HwidHash`, `AuthAccounts.HwidHash`)
- Added HWID fingerprint service in API:
  - `IHardwareFingerprintService`
  - `HardwareFingerprintService`
  - supports fallback normalization for legacy `hwidHash` payloads
- Updated public auth contract:
  - `POST /api/public/auth/login` now accepts `hwidFingerprint` (while keeping `hwidHash` for compatibility)
- Added admin endpoint to reset account HWID hash:
  - `POST /api/admin/bans/account/{user}/reset-hwid`
  - clears `AuthAccounts.HwidHash` and updates timestamp
- Added admin UI action:
  - `Reset account HWID` button in bans/account section
  - uses existing username/externalId input field
- Updated docs/examples/config:
  - `BivLauncher.Api.http` login payload + reset endpoint example
  - `backend/appsettings.json` added `Hwid:HmacSalt` key
  - `.env.example` clarified `HWID_HMAC_SALT` usage
- Verified builds:
  - `dotnet build backend/BivLauncher.sln`
  - `dotnet build launcher/BivLauncher.Launcher.sln`
  - `npm run build` (admin)
