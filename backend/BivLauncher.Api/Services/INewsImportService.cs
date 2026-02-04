using BivLauncher.Api.Contracts.Admin;

namespace BivLauncher.Api.Services;

public interface INewsImportService
{
    Task<NewsSourcesSyncResponse> SyncAsync(Guid? sourceId, CancellationToken cancellationToken = default);
}
