using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface ILauncherApiService
{
    Task<BootstrapResponse> GetBootstrapAsync(string apiBaseUrl, CancellationToken cancellationToken = default);
    Task<PublicAuthLoginResponse> LoginAsync(
        string apiBaseUrl,
        PublicAuthLoginRequest request,
        CancellationToken cancellationToken = default);
    Task<PublicAuthSessionResponse> GetSessionAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType = "Bearer",
        CancellationToken cancellationToken = default);
    Task<bool> HasSkinAsync(string apiBaseUrl, string username, CancellationToken cancellationToken = default);
    Task<bool> HasCapeAsync(string apiBaseUrl, string username, CancellationToken cancellationToken = default);
    Task<LauncherManifest> GetManifestAsync(string apiBaseUrl, string profileSlug, CancellationToken cancellationToken = default);
    Task<Stream> OpenAssetReadStreamAsync(string apiBaseUrl, string s3Key, CancellationToken cancellationToken = default);
    Task<PublicCrashReportCreateResponse> SubmitCrashReportAsync(
        string apiBaseUrl,
        PublicCrashReportCreateRequest request,
        CancellationToken cancellationToken = default);
    Task<PublicInstallTelemetryTrackResponse> SubmitInstallTelemetryAsync(
        string apiBaseUrl,
        PublicInstallTelemetryTrackRequest request,
        CancellationToken cancellationToken = default);
}
