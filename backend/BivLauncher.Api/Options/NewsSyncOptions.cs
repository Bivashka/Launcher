namespace BivLauncher.Api.Options;

public sealed class NewsSyncOptions
{
    public const string SectionName = "NewsSync";

    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 60;
}
