# Iteration 40: Custom Env File + Atomic Env Writes

Delivered:

- Added installer flag `--env-file <path>`:
  - installer can now operate on custom env paths (not only `.env`)
  - printed active env file path in startup output
  - JSON report now includes `config.envFile`
- Improved missing env-file handling:
  - when target env file is missing, installer creates it from `.env.example`
  - parent directory is created automatically
  - dry-run shows explicit message with selected env file path
- Replaced in-place `sed` updates with atomic writes:
  - env update now uses temp-file + rename strategy
  - prevents partial/truncated env writes on interruption
- Existing installer features (JSON report, redacted secrets, strict checks, health tuning) remain compatible.

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check --env-file .env.custom --output-json -`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
