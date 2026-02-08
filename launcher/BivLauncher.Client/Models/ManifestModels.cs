namespace BivLauncher.Client.Models;

public sealed class LauncherManifest
{
    public string ProfileSlug { get; set; } = string.Empty;
    public string BuildId { get; set; } = string.Empty;
    public string LoaderType { get; set; } = "vanilla";
    public string McVersion { get; set; } = "1.21.1";
    public string ClientVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string JvmArgsDefault { get; set; } = string.Empty;
    public string GameArgsDefault { get; set; } = string.Empty;
    public string? JavaRuntime { get; set; }
    public string? JavaRuntimeArtifactKey { get; set; }
    public string? JavaRuntimeArtifactSha256 { get; set; }
    public long? JavaRuntimeArtifactSizeBytes { get; set; }
    public string? JavaRuntimeArtifactContentType { get; set; }
    public string LaunchMode { get; set; } = "jar";
    public string LaunchMainClass { get; set; } = string.Empty;
    public List<string>? LaunchClasspath { get; set; } = [];
    public List<LauncherManifestFile> Files { get; set; } = [];
}

public sealed class LauncherManifestFile
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string S3Key { get; set; } = string.Empty;
}

public sealed class InstallProgressInfo
{
    public required string Message { get; init; }
    public int ProcessedFiles { get; init; }
    public int TotalFiles { get; init; }
    public int DownloadedFiles { get; init; }
    public int VerifiedFiles { get; init; }
    public string CurrentFilePath { get; init; } = string.Empty;
}

public sealed class InstallResult
{
    public required string InstanceDirectory { get; init; }
    public required int DownloadedFiles { get; init; }
    public required int VerifiedFiles { get; init; }
}

public sealed class LaunchResult
{
    public int ExitCode { get; init; }
    public string JavaExecutable { get; init; } = string.Empty;
    public bool Success => ExitCode == 0;
}
