namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsRetentionSettingsDto(
    bool Enabled,
    int MaxItems,
    int MaxAgeDays,
    DateTime? LastAppliedAtUtc,
    int LastDeletedItems,
    string LastError,
    DateTime? UpdatedAtUtc);
