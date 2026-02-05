# Release Checklist

## 1) Pre-release (local)

- Ensure working tree is clean for release commit.
- Run smoke checks:
  - Windows: `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -SkipApi`
  - Linux/macOS: `bash scripts/smoke.sh`
- Confirm backend tests pass: `dotnet test backend/BivLauncher.sln`
- Confirm admin build is green: `cd admin && npm run build`
- Confirm launcher build is green: `dotnet build launcher/BivLauncher.Launcher.sln`

## 2) Config and migration readiness

- Verify `.env` contains production values (no placeholders for `JWT_SECRET`, `HWID_HMAC_SALT`).
- Verify `docker-compose.yml` and installer flags for target host/ports.
- Verify EF migrations are included in commit (`backend/BivLauncher.Api/Data/Migrations`).

## 3) Deployment

- Run installer:
  - `bash install.sh --non-interactive --host <host> --api-port <port> --admin-port <port>`
- If needed, auto-create first admin:
  - add `--setup-admin --admin-user <user> --admin-password '<password>'`
- Check installer report and exit code.

## 4) Post-deploy smoke

- Health endpoint returns 200: `GET /health`.
- Admin login works.
- In admin wizard run pre-flight and verify checks complete.
- Verify storage probe passes.
- Verify auth provider probe passes (or ANY mode message is expected).
- Launcher bootstrap/login/manifest flow works.

## 5) Rollback readiness

- Keep latest env backup created by installer.
- Keep previous image tags/build artifacts for fast rollback.
- If deployment fails, use installer rollback options and restore env backup.
