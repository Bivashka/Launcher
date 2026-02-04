# Iteration 59: Audit Feed Sorting + Action Presets

Delivered:

- Extended admin audit logs API:
  - added `sort` query parameter (`desc` default, `asc` supported)
  - kept compatibility with `limit`, `offset`, `actionPrefix`, `actor`, `entityId`
- Runtime audit UI improvements:
  - sort selector (newest first / oldest first)
  - quick action-prefix presets:
    - `runtime*`
    - `runtime.cleanup`
    - `runtime.retention.run-from-preview`
    - `all`
  - pagination state now displays current sort mode
- Updated API HTTP examples to include sorted audit query.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
