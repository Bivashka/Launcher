# Iteration 42: Compose Env-file Awareness + Config Preflight

Delivered:

- Added compose config preflight before startup:
  - runs `docker compose --env-file <env> config -q`
  - fails early with:
    - exit code `23`
    - error code `compose-config-invalid`
- Added CLI flag:
  - `--skip-compose-config-check` to bypass this preflight.
- Ensured compose commands use selected env file:
  - `docker compose --env-file <env> up ...`
  - `docker compose --env-file <env> ps ...`
  - `docker compose --env-file <env> config -q`
- JSON report/summary now includes compose preflight check result as a meta check.
- Updated `composeCommand` preview to include `--env-file`.

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --env-file .env.custom --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
