# Iteration 39: Configurable Health Checks + Public IP Toggle

Delivered:

- Added installer flag `--no-public-ip`:
  - skips external lookup to `ifconfig.me`
  - useful for offline/air-gapped or privacy-sensitive environments
- Added health-check tuning flags:
  - `--health-retries <n>` (default `30`)
  - `--health-timeout <sec>` (default `3`)
  - `--health-delay <sec>` (default `2`)
- Added validation for health-check numeric options:
  - must be positive integers
  - invalid values return dedicated non-zero exit codes
- Extended JSON report:
  - `noPublicIp` flag state
  - `healthPolicy` block with retries/timeout/delay values
  - meta check record for public IP detection (`passed` / `skipped(...)`)
- Unified missing-argument handling for `--output-json` with structured `fail_now` path.

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --no-public-ip --health-retries 2 --health-timeout 1 --health-delay 1 --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
