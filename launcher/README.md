# BivLauncher Launcher (Avalonia)

Implemented MVP (iteration #4 foundation):

- C# / .NET 8
- Avalonia UI (MVVM)
- managed server list from backend bootstrap (`/api/public/bootstrap`)
- server icon rendering from bootstrap (`servers[].iconUrl` with fallback to `profiles[].iconUrl`)
- player login via public auth (`/api/public/auth/login`)
- login sends normalized HWID fingerprint, backend stores HMAC-SHA256 hash (`HWID_HMAC_SALT`)
- masked password input in login form (`PasswordChar`)
- full selected news text panel (list preview + full body reader)
- launcher self-update flow from bootstrap (`launcherUpdate.latestVersion` + `launcherUpdate.downloadUrl`) with download + install on restart
- real Discord Rich Presence integration in launcher runtime (idle/launching/in-game states from bootstrap RPC config)
- bundled Java runtime path is supplied via manifest (`javaRuntime`) from profile build settings
- if manifest provides `javaRuntimeArtifactKey` and runtime executable is missing, launcher downloads runtime artifact:
  - `.zip` artifact is extracted into instance directory
  - non-zip artifact is placed directly to `javaRuntime` path
- manifest launch strategy supports both:
  - `launchMode: jar` (`java -jar ...`)
  - `launchMode: mainclass` (`java -cp ... <mainClass>`) with classpath entries from manifest
- skin/cape presence check via `/api/public/skins/{user}` and `/api/public/capes/{user}`
- discord rpc config read from bootstrap (`profiles[].discordRpc`, `servers[].discordRpc`)
- manifest-based verify/install (`/api/public/manifest/{profileSlug}` + assets download)
- Java launch flow with RAM settings
- local settings file (`%AppData%/BivLauncher/settings.json`)
- debug mode live logs + crash summary + open logs folder

## Build / Run

```bash
dotnet build launcher/BivLauncher.Launcher.sln
dotnet run --project launcher/BivLauncher.Client/BivLauncher.Client.csproj
```
