# Iteration 31: Loader-aware Build Pipeline (Vanilla/Forge/Fabric/Quilt/NeoForge/LiteLoader)

Delivered:

- Added centralized loader catalog (`LoaderCatalog`) with supported values:
  - `vanilla`
  - `forge`
  - `fabric`
  - `quilt`
  - `neoforge`
  - `liteloader` (legacy optional)
- Added server loader validation in admin API:
  - create/update server now reject unsupported `loaderType`
- Extended rebuild pipeline to be loader-aware:
  - validates rebuild `loaderType` against supported catalog
  - supports layered source directories:
    1) `common/`
    2) `loaders/<loader>/common/`
    3) `loaders/<loader>/<mcVersion>/`
  - merges files by relative path with override priority (later layer wins)
  - fallback: profile root if layered structure is absent
- Admin dashboard improvements:
  - server form loader input switched from free-text to supported loader dropdown
  - rebuild options UI added:
    - loader type
    - MC version
    - source sub-path override
    - Java runtime path override
    - publish-to-servers toggle
- Updated build-sources docs with loader-aware directory layout and merge order.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
