# Build Sources

Place client files for each profile in:

- `build-sources/<profile-slug>/...`

Example:

- `build-sources/main-survival/mods/example-mod.jar`
- `build-sources/main-survival/config/some-config.toml`

Then call `POST /api/admin/profiles/{id}/rebuild` to upload files and publish manifest.

If profile uses bundled Java mode, set profile field `bundledJavaPath` (e.g. `runtime/bin/javaw.exe`)
and keep this executable path inside the same `build-sources/<profile-slug>/...` tree.

Alternative runtime delivery:

- upload runtime artifact via admin (`category=runtimes`, profile slug entityId)
- profile stores `bundledRuntimeKey`
- rebuild puts this value into manifest as `javaRuntimeArtifactKey`
- launcher can download/extract runtime artifact when `javaRuntime` file is missing.

Loader-aware structure (recommended):

- `build-sources/<profile-slug>/common/...` (shared for all loaders/versions)
- `build-sources/<profile-slug>/loaders/<loader>/common/...` (loader-specific shared files)
- `build-sources/<profile-slug>/loaders/<loader>/<mcVersion>/...` (loader + MC-version specific files)

Example:

- `build-sources/main-survival/common/config/base.toml`
- `build-sources/main-survival/loaders/fabric/common/mods/fabric-api.jar`
- `build-sources/main-survival/loaders/fabric/1.21.1/mods/my-mod.jar`

Merge order during rebuild (later overrides earlier on same path):

1. `common`
2. `loaders/<loader>/common`
3. `loaders/<loader>/<mcVersion>`

Launch strategy metadata (manifest):

- Rebuild request supports:
  - `launchMode`: `auto` / `jar` / `mainclass`
  - `launchMainClass`: required for explicit `mainclass`
  - `launchClasspath`: classpath entries (newline/`;`/`,` separated, supports globs)
- In `auto` mode:
  - `forge` / `neoforge` / `fabric` / `quilt` default to `mainclass`
  - other loaders default to `jar`
- Typical classpath entries:
  - `libraries/**/*.jar`
  - `*.jar`
