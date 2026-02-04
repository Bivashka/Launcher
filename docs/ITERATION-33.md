# Iteration 33: Installer.sh Interactive Setup Flow

Delivered:

- Extended `deploy/installer.sh` from simple `docker compose up` script into guided installer:
  - asks for API port
  - asks for Admin port
  - asks for public host/domain
  - asks whether to use HTTPS URL scheme
  - asks whether to enable MinIO compose profile
- Added `.env` read/write helpers in installer:
  - reads defaults from existing `.env`
  - writes selected values back to `.env`
  - updates:
    - `API_PORT`
    - `ADMIN_PORT`
    - `PUBLIC_BASE_URL`
    - `VITE_API_BASE_URL`
    - `ADMIN_ALLOWED_ORIGINS`
    - installer helper keys (`INSTALL_PUBLIC_HOST`, `INSTALL_USE_SSL`)
- Added non-interactive mode:
  - `BLP_INSTALLER_NON_INTERACTIVE=1 bash deploy/installer.sh`
  - reuses values from `.env` defaults without prompts
- Added MinIO profile support from installer:
  - `docker compose --profile minio up -d --build` when enabled
- Improved post-install output URLs:
  - prints configured host URLs
  - if host is localhost and public IP is detected, prints public URLs as extra hints

Verification:

- `bash -n deploy/installer.sh`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
