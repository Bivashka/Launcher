# Iteration 27: Real Discord Rich Presence in Launcher Runtime

Delivered:

- Added real launcher-side Discord RPC integration via NuGet package:
  - `DiscordRichPresence` (`DiscordRPC.dll`)
- Added dedicated launcher service:
  - `IDiscordRpcService`
  - `DiscordRpcService`
  - handles lifecycle of Discord RPC client per selected server/appId
  - safely clears/disposes presence when server is not selected or RPC is disabled
- Extended launcher server runtime model with full RPC asset fields:
  - `discordRpcLargeImageKey`
  - `discordRpcLargeImageText`
  - `discordRpcSmallImageKey`
  - `discordRpcSmallImageText`
- Wired view model flow to runtime presence updates:
  - when selected server changes -> idle presence
  - before game run -> launching presence
  - while game starts/runs -> in-game presence
  - after game exit -> back to idle presence
- Registered RPC service in DI container (`App.axaml.cs`).
- Verified solution builds:
  - `dotnet build launcher/BivLauncher.Launcher.sln`
  - `dotnet build backend/BivLauncher.sln`
