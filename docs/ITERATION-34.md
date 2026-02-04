# Iteration 34: Installer CLI Flags + Secret Auto-generation + Health Checks

Delivered:

- Extended `deploy/installer.sh` with CLI options:
  - `--non-interactive`
  - `--with-minio`
  - `--host <host>`
  - `--api-port <port>`
  - `--admin-port <port>`
  - `--ssl <true|false>`
  - `--skip-health-check`
  - `--help`
- Added argument validation:
  - required values for flags that expect arguments
  - API/Admin port range validation (`1..65535`)
  - boolean validation for SSL/MinIO inputs
- Added secure secret bootstrap:
  - if `.env` has empty/default placeholders, installer generates and persists:
    - `JWT_SECRET`
    - `HWID_HMAC_SALT`
- Added post-start health checks (unless skipped):
  - API: `http://localhost:<API_PORT>/health`
  - Admin: `http://localhost:<ADMIN_PORT>`
- Kept guided interactive flow and `.env` update logic from previous iteration.

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
