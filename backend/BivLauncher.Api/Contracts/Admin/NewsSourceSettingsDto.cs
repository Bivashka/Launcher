namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsSourceSettingsDto(
    Guid Id,
    string Name,
    string Type,
    string Url,
    bool Enabled,
    int MaxItems,
    int MinFetchIntervalMinutes,
    DateTime? LastFetchAttemptAtUtc,
    DateTime? LastSyncAtUtc,
    DateTime? LastContentChangeAtUtc,
    string LastSyncError,
    DateTime UpdatedAtUtc);
