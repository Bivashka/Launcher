# Iteration 46: Runtime Artifact Binding + Manifest Runtime Install Hook

Delivered:

- Added profile-level runtime artifact key:
  - DB entity: `Profile.BundledRuntimeKey`
  - runtime metadata fields:
    - `Profile.BundledRuntimeSha256`
    - `Profile.BundledRuntimeSizeBytes`
    - `Profile.BundledRuntimeContentType`
  - EF migrations:
    - `AddProfileBundledRuntimeKey`
    - `AddProfileBundledRuntimeMetadata`
  - admin profile contracts:
    - `ProfileDto.bundledRuntimeKey`
    - `ProfileDto.bundledRuntimeSha256`
    - `ProfileDto.bundledRuntimeSizeBytes`
    - `ProfileDto.bundledRuntimeContentType`
    - `ProfileUpsertRequest.bundledRuntimeKey`
- Extended admin runtime upload behavior:
  - runtime upload still goes to `runtimes/<profileSlug>/...`
  - backend now auto-links uploaded runtime key to profile `BundledRuntimeKey` by slug
  - backend now stores runtime metadata on upload (sha256, size, content type)
- Extended rebuild pipeline + manifest:
  - rebuild request supports optional `javaRuntimeArtifactKey` override
  - manifest now includes `javaRuntimeArtifactKey`
  - resolution order:
    1) rebuild request override
    2) profile `BundledRuntimeKey`
- Launcher install flow update:
  - if manifest has `javaRuntime` and runtime file is missing:
    - downloads `javaRuntimeArtifactKey`
    - if artifact is `.zip`: extracts into instance dir
    - otherwise places artifact directly at `javaRuntime` path
  - if old manifest has no `javaRuntimeArtifactKey`, launcher skips runtime auto-install and keeps backward compatibility
  - validates runtime file existence after install step
- Admin UI updates:
  - profile form includes `Bundled runtime artifact key`
  - profiles list shows runtime key
  - runtime upload panel now refreshes admin data and updates profile form when slug matches

Verification:

- `dotnet ef migrations add AddProfileBundledRuntimeKey` (already generated)
- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
