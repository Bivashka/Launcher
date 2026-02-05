namespace BivLauncher.Api.Options;

public sealed class DeveloperSupportOptions
{
    public const string SectionName = "DeveloperSupport";

    public string DisplayName { get; set; } = "Bivashka";
    public string Telegram { get; set; } = "https://t.me/bivashka";
    public string Discord { get; set; } = "bivashka";
    public string Website { get; set; } = "https://github.com/bivashka";
    public string Notes { get; set; } = "Official developer support contact. Not editable from admin UI.";
}
