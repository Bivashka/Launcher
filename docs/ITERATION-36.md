# Iteration 36: Installer Dry-run Mode

Delivered:

- Added `--dry-run` to `deploy/installer.sh`.
- In dry-run mode installer now:
  - does not require Docker/Docker Compose availability
  - does not create or modify `.env`
  - prints which `.env` keys would be updated
  - prints which `docker compose ... up -d --build` command would run
  - skips post-start health checks with explicit dry-run message
- Added URL host formatting for IPv6 addresses (wraps host in `[]` in printed URLs).
- Added dry-run details to installer help and README.

Verification:

- `C:\Program Files\Git\bin\bash.exe -n deploy/installer.sh`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --help`
- `C:\Program Files\Git\bin\bash.exe deploy/installer.sh --dry-run --non-interactive --skip-health-check`
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
