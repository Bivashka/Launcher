# Iteration 29: Full Launcher Self-update Flow (Download + Install + Restart)

Delivered:

- Added launcher update service:
  - `ILauncherUpdateService`
  - `LauncherUpdateService`
- Implemented update package download in launcher:
  - HTTP streaming download with progress reporting
  - stores package under `%AppData%/BivLauncher/updates/<version>/...`
  - validates update archive format (`.zip`)
- Implemented safe update apply on restart:
  - generates helper PowerShell script (`apply-update.ps1`)
  - helper waits for launcher process exit
  - extracts update archive, copies files into launcher directory
  - starts updated launcher executable
- Extended launcher settings service with update directory resolver:
  - `GetUpdatesDirectory()`
- Extended launcher update UI/VM:
  - `Download update` action
  - `Install and restart` action
  - download progress/status text
  - install action available only after successful download
- Added DI registration for update service in `App.axaml.cs`.
- Verified builds:
  - `dotnet build launcher/BivLauncher.Launcher.sln`
  - `dotnet build backend/BivLauncher.sln`

Notes:

- Update package must be provided as a ZIP archive in `launcherUpdate.downloadUrl`.
- On dev runs through `dotnet`, install step may require running published launcher executable.
