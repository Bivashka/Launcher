# Iteration 5: Public Auth Provider Integration

Delivered:

- New public auth endpoint:
  - `POST /api/public/auth/login`
- External auth integration service (`IExternalAuthService`):
  - calls configurable provider login URL
  - tolerant JSON parsing for different provider payload formats
  - optional dev fallback mode
- New DB entity:
  - `AuthAccount` (externalId, username, roles, hwidHash, banned, timestamps)
- JWT player tokens:
  - issued after successful auth via existing JWT secret
- Launcher integration:
  - account panel (username/password/login)
  - login status in UI
  - verify/play commands enabled only after successful login
  - player username persisted in launcher settings

Configuration:

- `AUTH_PROVIDER_LOGIN_URL`
- `AUTH_PROVIDER_TIMEOUT_SECONDS`
- `AUTH_PROVIDER_ALLOW_DEV_FALLBACK`
- `AUTH_PROVIDER_URLS` (legacy/combined source; supports `login=<url>`)
