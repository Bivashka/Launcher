namespace BivLauncher.Api.Data.Entities;

public sealed class ProjectInstallStat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProjectKey { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string LastLauncherVersion { get; set; } = string.Empty;
    public int SeenCount { get; set; }
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
