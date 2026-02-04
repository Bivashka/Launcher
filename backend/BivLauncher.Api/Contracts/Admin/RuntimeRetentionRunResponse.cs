namespace BivLauncher.Api.Contracts.Admin;

public sealed record RuntimeRetentionRunResponse(
    bool Applied,
    int ProfilesProcessed,
    int DeletedItems,
    DateTime AppliedAtUtc,
    string Error);
