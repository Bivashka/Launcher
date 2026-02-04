namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsRetentionRunResponse(
    bool Applied,
    int DeletedItems,
    int RemainingItems,
    DateTime AppliedAtUtc,
    string Error);
