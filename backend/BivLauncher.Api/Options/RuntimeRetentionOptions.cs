namespace BivLauncher.Api.Options;

public sealed class RuntimeRetentionOptions
{
    public const string SectionName = "RuntimeRetention";

    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 360;
    public int KeepLast { get; set; } = 3;
}
