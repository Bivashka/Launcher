namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsSourceSettingsDto(
    Guid Id,
    string Name,
    string Type,
    string Url,
    bool Enabled,
    int MaxItems,
    DateTime? LastSyncAtUtc,
    string LastSyncError,
    DateTime UpdatedAtUtc);
