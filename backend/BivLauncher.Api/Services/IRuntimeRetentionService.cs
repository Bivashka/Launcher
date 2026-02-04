using BivLauncher.Api.Contracts.Admin;

namespace BivLauncher.Api.Services;

public interface IRuntimeRetentionService
{
    Task<RuntimeRetentionDryRunResponse> PreviewRetentionAsync(
        string? profileSlug = null,
        int maxProfiles = 20,
        int previewKeysLimit = 10,
        CancellationToken cancellationToken = default);
    Task<RuntimeRetentionRunResponse> ApplyRetentionFromPreviewAsync(
        string? profileSlug = null,
        int maxProfiles = 20,
        CancellationToken cancellationToken = default);
    Task<RuntimeRetentionRunResponse> ApplyRetentionAsync(CancellationToken cancellationToken = default);
}
