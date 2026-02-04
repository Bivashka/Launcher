namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsSyncSettingsDto(
    bool Enabled,
    int IntervalMinutes,
    DateTime? LastRunAtUtc,
    string LastRunError,
    DateTime? UpdatedAtUtc);
