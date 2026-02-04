# Iteration 8: Discord RPC Config Pipeline (Backend + Launcher Read)

Delivered:

- New DB entity:
  - `DiscordRpcConfig` with scope (`profile`/`server`) and RPC fields.
- Admin endpoints:
  - `GET/PUT/DELETE /api/admin/discord-rpc/profile/{profileId}`
  - `GET/PUT/DELETE /api/admin/discord-rpc/server/{serverId}`
- Public bootstrap now includes Discord RPC config:
  - `profiles[].discordRpc`
  - `profiles[].servers[].discordRpc`
- Launcher integration:
  - reads RPC config from bootstrap
  - applies server-level override over profile-level fallback
  - shows RPC preview text in server list (`RPC appId/details/state`)

Notes:

- This iteration wires config transport and consumption.
- Actual native Discord Rich Presence process integration can be added next.
