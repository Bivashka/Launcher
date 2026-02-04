# Iteration 38: CI-friendly Installer Exit/Error Reporting

Delivered:

- Enhanced `deploy/installer.sh` with CI-oriented failure handling.
- Added structured failure metadata:
  - `result.status`
  - `result.exitCode`
  - `result.errorCode`
- Added dedicated exit codes for major failure classes, including:
  - missing required option values / unknown options
  - invalid ports / invalid boolean flags
  - Docker missing (`20`)
  - Docker Compose plugin missing (`21`)
  - strict smoke-check failure (`30`)
- Extended JSON report with `checkSummary`:
  - total/passed/failed/skipped
  - grouped subtotals for `service`, `http`, `meta`
- Kept backward compatibility:
  - still includes detailed `checks[]`
  - still includes top-level `failedChecks`
- Sensitive env values remain redacted in report (`<redacted>`).

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
