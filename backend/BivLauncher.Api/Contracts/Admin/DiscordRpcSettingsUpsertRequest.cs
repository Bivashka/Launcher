namespace BivLauncher.Api.Contracts.Admin;

public sealed class DiscordRpcSettingsUpsertRequest
{
    public bool Enabled { get; set; } = true;
    public bool PrivacyMode { get; set; }
}
