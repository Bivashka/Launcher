# Iteration 41: Port Availability Pre-checks

Delivered:

- Added installer local port pre-check before `docker compose up`.
- Checks required host ports:
  - API (`API_PORT`)
  - Admin (`ADMIN_PORT`)
  - Postgres (`POSTGRES_PORT`)
  - MinIO API/Console (`MINIO_PORT`, `MINIO_CONSOLE_PORT`) when `--with-minio`
- Added CLI flag:
  - `--skip-port-check` to bypass this pre-check
- Added port-check results to JSON report (`checks[]` + summary counters).
- Extended JSON config block with:
  - `postgresPort`
  - `minioPort`
  - `minioConsolePort`
- Uses available local tooling (`ss` / `lsof` / `netstat`) and marks check as `skipped(no-tool)` if none found.
- If a required port is busy, installer fails early with:
  - exit code `22`
  - error code `port-in-use`

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --env-file .env.custom --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
