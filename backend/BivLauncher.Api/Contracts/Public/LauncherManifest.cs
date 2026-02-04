namespace BivLauncher.Api.Contracts.Public;

public sealed record LauncherManifest(
    string ProfileSlug,
    string BuildId,
    string LoaderType,
    string McVersion,
    string ClientVersion,
    DateTime CreatedAtUtc,
    string JvmArgsDefault,
    string GameArgsDefault,
    string? JavaRuntime,
    string? JavaRuntimeArtifactKey,
    string? JavaRuntimeArtifactSha256,
    long? JavaRuntimeArtifactSizeBytes,
    string? JavaRuntimeArtifactContentType,
    IReadOnlyList<LauncherManifestFile> Files,
    string LaunchMode = "jar",
    string LaunchMainClass = "",
    IReadOnlyList<string>? LaunchClasspath = null);

public sealed record LauncherManifestFile(
    string Path,
    string Sha256,
    long Size,
    string S3Key);
