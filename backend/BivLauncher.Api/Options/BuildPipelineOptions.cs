namespace BivLauncher.Api.Options;

public sealed class BuildPipelineOptions
{
    public const string SectionName = "BuildPipeline";

    public string SourceRoot { get; set; } = "BuildSources";
    public string DefaultJvmArgs { get; set; } = "-Xms1024M -Xmx2048M";
    public string DefaultGameArgs { get; set; } = string.Empty;
}
