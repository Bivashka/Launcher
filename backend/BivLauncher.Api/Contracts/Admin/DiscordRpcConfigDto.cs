namespace BivLauncher.Api.Contracts.Admin;

public sealed record DiscordRpcConfigDto(
    Guid Id,
    string ScopeType,
    Guid ScopeId,
    bool Enabled,
    string AppId,
    string DetailsText,
    string StateText,
    string LargeImageKey,
    string LargeImageText,
    string SmallImageKey,
    string SmallImageText,
    DateTime UpdatedAtUtc);
