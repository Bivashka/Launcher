# Iteration 4: Launcher MVP (Avalonia)

Delivered:

- New launcher solution:
  - `launcher/BivLauncher.Launcher.sln`
  - `launcher/BivLauncher.Client` (.NET 8 + Avalonia MVVM)
- Services layer:
  - settings persistence (`settings.json` in `%AppData%/BivLauncher`)
  - bootstrap + manifest + asset API client
  - manifest verify/install pipeline with SHA256 checks
  - Java process launcher with live stdout/stderr capture
  - structured launcher logs (`%AppData%/BivLauncher/logs/launcher.log`)
- UI flow:
  - managed servers list from backend bootstrap only
  - launcher settings (API URL, install dir, Java mode, RAM)
  - actions: refresh, save settings, verify files, play
  - debug mode live logs
  - crash summary + copy/open logs controls

Notes:

- The Java launch command currently uses `-jar <detected-jar>` strategy for MVP.
- Modloader-specific runtime composition will be added in later iterations.
