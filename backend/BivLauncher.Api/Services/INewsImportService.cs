using BivLauncher.Api.Contracts.Admin;

namespace BivLauncher.Api.Services;

public interface INewsImportService
{
    Task<NewsSourcesSyncResponse> SyncAsync(Guid? sourceId, bool force = false, CancellationToken cancellationToken = default);
}
