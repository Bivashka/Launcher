namespace BivLauncher.Api.Options;

public sealed class InstallTelemetryOptions
{
    public const string SectionName = "InstallTelemetry";

    public bool Enabled { get; set; } = true;
}
