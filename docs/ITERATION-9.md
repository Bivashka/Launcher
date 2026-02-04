# Iteration 9: Discord RPC Admin UI + Launcher Preview

Delivered:

- Admin UI now includes a `Discord RPC` panel with:
  - scope selection (`profile` / `server`)
  - load/save/delete config actions
  - all common RPC fields (appId/details/state/images)
- Backend added scoped Discord RPC config entity and API:
  - `GET/PUT/DELETE /api/admin/discord-rpc/profile/{profileId}`
  - `GET/PUT/DELETE /api/admin/discord-rpc/server/{serverId}`
- Public bootstrap now includes RPC config payloads.
- Launcher reads and displays effective RPC preview per server:
  - server config overrides profile config
  - shown in server list as `RPC appId/details/state`

Migration:

- `AddDiscordRpcConfigs`
