# Iteration 44: Env Rollback-on-Fail + Robust Failure Paths

Delivered:

- Added installer flag:
  - `--rollback-env-on-fail`
  - when enabled, installer restores target env file from backup if failure happens after writes
- Extended JSON report:
  - `envRollbackOnFail`
  - `envRollbackApplied`
  - rollback result recorded in `checks` as meta item
- Hardened failure handling for critical write/start steps with structured `fail_now`:
  - env file creation failure (`19`)
  - compose up failure (`25`)
  - atomic env write failure (`26`)
- Improved resilience:
  - `docker compose up` is now wrapped and returns structured failure (instead of abrupt shell exit)
  - atomic env write path now validates each I/O step and reports precise failure

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --env-file .env.custom --rollback-env-on-fail --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
