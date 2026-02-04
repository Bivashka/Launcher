namespace BivLauncher.Api.Contracts.Admin;

public sealed record ProfileDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    bool Enabled,
    string IconKey,
    int Priority,
    int RecommendedRamMb,
    string BundledJavaPath,
    string BundledRuntimeKey,
    string BundledRuntimeSha256,
    long BundledRuntimeSizeBytes,
    string BundledRuntimeContentType,
    string LatestBuildId,
    string LatestManifestKey,
    string LatestClientVersion,
    DateTime CreatedAtUtc);
