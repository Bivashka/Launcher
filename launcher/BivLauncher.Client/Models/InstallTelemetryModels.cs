namespace BivLauncher.Client.Models;

public sealed class PublicInstallTelemetryTrackRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string LauncherVersion { get; set; } = string.Empty;
}

public sealed class PublicInstallTelemetryTrackResponse
{
    public bool Accepted { get; set; }
    public bool Enabled { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
