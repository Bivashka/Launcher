namespace BivLauncher.Api.Contracts.Admin;

public sealed class ServerLauncherJarBuildRequest
{
    public string Version { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string AuthlibVersion { get; set; } = string.Empty;
    public string AuthlibSourceUrl { get; set; } = string.Empty;
}
