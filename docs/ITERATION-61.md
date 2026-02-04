# Iteration 61: Full Admin Audit Coverage

Delivered:

- Extended admin audit logging beyond runtime flows to cover most mutating admin actions.
- Added audit events for admin auth:
  - `admin.setup`
  - `admin.login`
  - `admin.login.failed`
- Added audit events for CRUD and operational actions:
  - Profiles: `profile.create`, `profile.update`, `profile.delete`, `profile.rebuild`, `profile.rebuild.failed`
  - Servers: `server.create`, `server.update`, `server.delete`
  - News: `news.create`, `news.update`, `news.delete`
  - Bans: `ban.hwid.create`, `ban.account.create`, `ban.account.reset-hwid`, `ban.delete`
  - Cosmetics: `cosmetics.skin.upload`, `cosmetics.cape.upload`
  - Generic uploads: `<category>.upload` for non-runtime categories via `/api/admin/upload`
- Added audit events for settings and background actions:
  - `settings.auth-provider.update`
  - `settings.branding.update`
  - `settings.s3.update` (without secret values in details)
  - `news.sources.update`, `news.sources.sync`
  - `news.sync.settings.update`, `news.sync.run`, `news.sync.run.failed`
  - `news.retention.settings.update`, `news.retention.run`, `news.retention.dry-run`
- Admin UI audit panel improvements:
  - renamed block to **Admin Audit Logs**
  - expanded action presets for `profiles*`, `servers*`, `news*`, `bans*`, `settings*`, `auth*` (+ existing runtime presets)
- Updated root README to iteration `#61`.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `npm run build` (admin)
