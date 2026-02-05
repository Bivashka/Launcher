using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface IPendingSubmissionService
{
    Task EnqueueCrashReportAsync(
        string apiBaseUrl,
        PublicCrashReportCreateRequest request,
        CancellationToken cancellationToken = default);

    Task EnqueueInstallTelemetryAsync(
        string apiBaseUrl,
        PublicInstallTelemetryTrackRequest request,
        CancellationToken cancellationToken = default);

    Task<PendingSubmissionFlushResult> FlushAsync(
        Func<PendingSubmissionItem, CancellationToken, Task<bool>> sender,
        CancellationToken cancellationToken = default);
}
