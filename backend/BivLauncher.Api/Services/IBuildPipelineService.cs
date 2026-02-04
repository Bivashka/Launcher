using BivLauncher.Api.Contracts.Admin;

namespace BivLauncher.Api.Services;

public interface IBuildPipelineService
{
    Task<BuildDto> RebuildProfileAsync(Guid profileId, ProfileRebuildRequest request, CancellationToken cancellationToken = default);
}
