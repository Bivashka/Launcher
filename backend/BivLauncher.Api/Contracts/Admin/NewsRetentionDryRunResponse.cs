namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsRetentionDryRunResponse(
    bool Enabled,
    int MaxItems,
    int MaxAgeDays,
    int TotalItems,
    int WouldDeleteByAge,
    int WouldDeleteByOverflow,
    int WouldDeleteTotal,
    int WouldRemainItems,
    DateTime CalculatedAtUtc);
