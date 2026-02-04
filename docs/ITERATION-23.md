# Iteration 23: Launcher Server Icons in UI

Delivered:

- Added server icon support in launcher server list:
  - reads `servers[].iconUrl` from bootstrap
  - falls back to `profiles[].iconUrl` when server icon is not set
- Extended launcher view model icon pipeline:
  - resolves relative icon paths against API base URL
  - downloads icon image over HTTP
  - caches loaded icons in-memory to avoid repeated downloads on refresh
- Extended `ManagedServerItem` model with `Icon` field (`IImage?`).
- Updated server list XAML template:
  - added icon slot (32x32)
  - kept existing server metadata layout (name/address/loader/RPC preview)
- Updated top-level docs state to iteration `#23`.
