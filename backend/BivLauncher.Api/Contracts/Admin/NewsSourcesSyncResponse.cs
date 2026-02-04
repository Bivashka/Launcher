namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsSourcesSyncResponse(
    int SourcesProcessed,
    int Imported,
    IReadOnlyList<NewsSourceSyncResultDto> Results);

public sealed record NewsSourceSyncResultDto(
    Guid SourceId,
    string Name,
    string Type,
    int Imported,
    string Error);
