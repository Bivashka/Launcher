# Iteration 35: Installer Smoke Checks + Strict Mode

Delivered:

- Upgraded `deploy/installer.sh` post-start checks into smoke-check flow:
  - verifies docker compose service state for:
    - `api`
    - `admin`
    - `minio` (when `--with-minio` is used)
  - verifies HTTP endpoints:
    - `GET /health`
    - `GET /api/admin/setup/status`
    - admin root URL
- Added strict mode:
  - `--strict-check` makes installer exit with non-zero code when any smoke check fails.
- Kept opt-out mode:
  - `--skip-health-check` disables all post-start checks.
- Updated installer help/usage text with new strict-check option.

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
