namespace BivLauncher.Api.Options;

public sealed class NewsRetentionOptions
{
    public const string SectionName = "NewsRetention";

    public bool Enabled { get; set; }
    public int MaxItems { get; set; } = 500;
    public int MaxAgeDays { get; set; } = 30;
}
