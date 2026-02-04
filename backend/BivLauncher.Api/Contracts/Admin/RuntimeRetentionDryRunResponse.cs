namespace BivLauncher.Api.Contracts.Admin;

public sealed record RuntimeRetentionDryRunResponse(
    bool Enabled,
    int IntervalMinutes,
    int KeepLast,
    string ProfileSlugFilter,
    int MaxProfiles,
    int PreviewKeysLimit,
    int ProfilesScanned,
    int ProfilesWithDeletions,
    int ProfilesReturned,
    bool HasMoreProfiles,
    int TotalDeleteCandidates,
    IReadOnlyList<RuntimeRetentionProfileDryRunItem> Profiles,
    DateTime CalculatedAtUtc);

public sealed record RuntimeRetentionProfileDryRunItem(
    string ProfileSlug,
    int TotalRuntimeObjects,
    int KeepCount,
    int DeleteCount,
    IReadOnlyList<string> DeleteKeysPreview,
    bool HasMoreDeleteKeys);
