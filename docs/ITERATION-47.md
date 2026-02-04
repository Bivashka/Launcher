# Iteration 47: Runtime Artifact Integrity Metadata + Launcher Verification

Delivered:

- Extended public launcher manifest runtime section with optional integrity metadata:
  - `javaRuntimeArtifactSha256`
  - `javaRuntimeArtifactSizeBytes`
  - `javaRuntimeArtifactContentType`
- Build pipeline now injects runtime metadata into manifest when runtime artifact key used in rebuild matches profile-bound runtime key.
- Launcher runtime installer now validates downloaded runtime artifact:
  - checks size when `javaRuntimeArtifactSizeBytes` is present
  - checks SHA-256 when `javaRuntimeArtifactSha256` is present
  - fails install if integrity checks mismatch
- Backward compatibility preserved:
  - old manifests without runtime artifact metadata still work
  - old manifests without `javaRuntimeArtifactKey` still skip runtime auto-install path without crash

Verification:

- `dotnet build backend/BivLauncher.sln`
- `dotnet build launcher/BivLauncher.Launcher.sln`
- `npm run build` (admin)
