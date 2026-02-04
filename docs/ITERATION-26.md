# Iteration 26: Launcher Self-update Check (Bootstrap + Download CTA)

Delivered:

- Extended public bootstrap response with optional update block:
  - `launcherUpdate.latestVersion`
  - `launcherUpdate.downloadUrl`
  - `launcherUpdate.releaseNotes`
- Added backend update-config resolution from env/appsettings:
  - `LAUNCHER_LATEST_VERSION`
  - `LAUNCHER_UPDATE_URL`
  - `LAUNCHER_RELEASE_NOTES`
  - fallback section: `LauncherUpdate:*` in `appsettings.json`
- Added launcher-side update detection:
  - reads current launcher version from assembly metadata
  - compares current version with bootstrap latest version
  - marks update as available only when latest version is newer and URL is provided
- Added launcher UI update panel:
  - current/latest version display
  - availability status
  - release notes area (if provided)
  - button to open update download page
- Updated deployment wiring:
  - `.env.example` new launcher update vars
  - `docker-compose.yml` passes launcher update vars into API container
- Verified builds:
  - `dotnet build backend/BivLauncher.sln`
  - `dotnet build launcher/BivLauncher.Launcher.sln`
