# Iteration 37: Installer JSON Report Output

Delivered:

- Added installer option: `--output-json <path|->`.
  - Writes final execution report as JSON to file.
  - `--output-json -` prints JSON report to stdout.
- JSON report includes:
  - execution mode flags (`dryRun`, `nonInteractive`, `strictCheck`, `skipHealthCheck`)
  - compose command to be executed
  - resolved config values (ports/host/scheme/public URLs)
  - detected public IP (if available)
  - generated secret keys list
  - `.env` key changes performed/planned
  - smoke check results and total failed checks
- Sensitive values (`JWT_SECRET`, `HWID_HMAC_SALT`, `S3_SECRET_KEY`) are redacted in `envChanges`.
- Improved installer reporting internals:
  - tracks env key updates as structured entries
  - tracks each service/http/smoke check result
  - writes JSON report even on strict-check failure path

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
