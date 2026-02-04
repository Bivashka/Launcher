using BivLauncher.Api.Contracts.Admin;

namespace BivLauncher.Api.Services;

public interface INewsRetentionService
{
    Task<NewsRetentionRunResponse> ApplyRetentionAsync(CancellationToken cancellationToken = default);
    Task<NewsRetentionDryRunResponse> PreviewRetentionAsync(CancellationToken cancellationToken = default);
}
