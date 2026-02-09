namespace BivLauncher.Api.Contracts.Admin;

public sealed class LauncherBuildRequest
{
    public string RuntimeIdentifier { get; set; } = "win-x64";
    public List<string> RuntimeIdentifiers { get; set; } = [];
    public string Configuration { get; set; } = "Release";
    public bool SelfContained { get; set; } = true;
    public bool PublishSingleFile { get; set; } = true;
    public string Version { get; set; } = string.Empty;
    public bool AutoPublishUpdate { get; set; } = true;
    public string ReleaseNotes { get; set; } = string.Empty;
}
