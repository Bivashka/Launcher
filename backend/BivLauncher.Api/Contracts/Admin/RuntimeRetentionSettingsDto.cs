namespace BivLauncher.Api.Contracts.Admin;

public sealed record RuntimeRetentionSettingsDto(
    bool Enabled,
    int IntervalMinutes,
    int KeepLast,
    DateTime? LastRunAtUtc,
    int LastDeletedItems,
    int LastProfilesProcessed,
    string LastRunError,
    DateTime? UpdatedAtUtc);
