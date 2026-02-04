# Iteration 32: Manifest Launch Strategy Metadata (jar/mainclass + classpath)

Delivered:

- Extended rebuild request contract:
  - `launchMode`: `auto` / `jar` / `mainclass`
  - `launchMainClass`: optional (required for explicit `mainclass`)
  - `launchClasspath`: optional text list (newline / `;` / `,` separators)
- Extended public manifest contract:
  - `launchMode`
  - `launchMainClass`
  - `launchClasspath[]`
- Build pipeline launch profile resolution:
  - validates and normalizes launch mode/classpath
  - rejects absolute and traversal paths in classpath entries
  - `auto` mode defaults:
    - `forge` / `neoforge` -> `cpw.mods.modlauncher.Launcher`
    - `fabric` -> `net.fabricmc.loader.impl.launch.knot.KnotClient`
    - `quilt` -> `org.quiltmc.loader.impl.launch.knot.KnotClient`
    - other loaders -> `jar`
  - default classpath in auto mainclass mode: `libraries/**/*.jar`, `*.jar`
- Launcher runtime launch flow updated:
  - `jar` mode: keeps `-jar` execution
  - `mainclass` mode: uses `-cp` + main class
  - classpath resolver supports:
    - direct relative file entries
    - simple globs (`*`, `?`)
    - recursive globs (`/**/`)
  - classpath path safety checks stay inside instance directory
- Admin UI rebuild options updated with launch strategy controls.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
