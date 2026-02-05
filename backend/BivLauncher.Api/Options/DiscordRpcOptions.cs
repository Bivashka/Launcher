namespace BivLauncher.Api.Options;

public sealed class DiscordRpcOptions
{
    public const string SectionName = "DiscordRpc";

    public bool Enabled { get; set; } = true;
    public bool PrivacyMode { get; set; } = false;
}
