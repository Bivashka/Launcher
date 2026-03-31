namespace BivLauncher.Api.Options;

public sealed class SecuritySettingsOptions
{
    public const string SectionName = "Security";

    public string FilePath { get; set; } = "security-settings.json";
}
