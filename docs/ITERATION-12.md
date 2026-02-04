# Iteration 12: Launcher I18N (RU default + EN/UK/KK switch)

Delivered:

- Launcher UI fully moved to localized text bindings (no hardcoded UI labels in XAML).
- Added language switch in launcher settings:
  - default: `ru`
  - supported: `ru`, `en`, `uk`, `kk`
- Added localization catalog and runtime language switching:
  - section titles
  - button labels
  - field labels
  - status texts and runtime messages from ViewModel
  - RAM/skin/cape/crash state labels
  - Discord RPC preview and news meta labels
- Added persistence of selected language to local launcher settings.
- Updated install progress model to provide file path and localized progress text in UI.

Files:

- `launcher/BivLauncher.Client/Services/LauncherLocalization.cs`
- `launcher/BivLauncher.Client/Models/LocalizedOption.cs`
- `launcher/BivLauncher.Client/ViewModels/MainWindowViewModel.cs`
- `launcher/BivLauncher.Client/Views/MainWindow.axaml`
- `launcher/BivLauncher.Client/Models/LauncherSettings.cs`
- `launcher/BivLauncher.Client/Services/SettingsService.cs`
- `launcher/BivLauncher.Client/Models/ManifestModels.cs`
- `launcher/BivLauncher.Client/Services/ManifestInstallerService.cs`
