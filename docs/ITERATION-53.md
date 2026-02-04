# Iteration 53: Runtime Retention Dry-Run Filters + JSON Export

Delivered:

- Extended runtime retention dry-run API with filters:
  - `profileSlug` (optional)
  - `maxProfiles` (1..200)
  - `previewKeysLimit` (1..100)
- Dry-run response now includes:
  - selected filter values
  - `profilesReturned` and `hasMoreProfiles`
  - filtered profile preview list
- Runtime retention service updated to:
  - compute filtered dry-run plans without deletion
  - keep full total candidate counters while limiting returned previews
- Admin UI runtime retention section updated:
  - dry-run filter inputs (profile slug, max profiles, preview key limit)
  - dry-run JSON export button
  - richer summary (returned profiles + hidden profiles indicator)
- Updated API HTTP examples for filtered runtime retention dry-run.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
