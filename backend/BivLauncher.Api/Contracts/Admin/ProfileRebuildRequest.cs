using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class ProfileRebuildRequest
{
    [MaxLength(32)]
    public string LoaderType { get; set; } = "vanilla";

    [MaxLength(32)]
    public string McVersion { get; set; } = "1.21.1";

    [MaxLength(64)]
    public string ClientVersion { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string JvmArgsDefault { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string GameArgsDefault { get; set; } = string.Empty;

    [MaxLength(512)]
    public string JavaRuntimePath { get; set; } = string.Empty;

    [MaxLength(512)]
    public string JavaRuntimeArtifactKey { get; set; } = string.Empty;

    [MaxLength(16)]
    public string LaunchMode { get; set; } = "auto";

    [MaxLength(512)]
    public string LaunchMainClass { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string LaunchClasspath { get; set; } = string.Empty;

    [MaxLength(256)]
    public string SourceSubPath { get; set; } = string.Empty;

    public bool PublishToServers { get; set; } = true;
}
