using BivLauncher.Client.Models;
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class LauncherApiService : ILauncherApiService
{
    private const int MaxRetryAttempts = 3;
    private const int ManifestChunkSizeBytes = 16 * 1024;
    private const int AssetChunkSizeBytes = 256 * 1024;
    private static readonly TimeSpan ApiRequestAttemptTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MetadataRequestAttemptTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ManifestRequestAttemptTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ManifestChunkRequestAttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AssetChunkRequestAttemptTimeout = TimeSpan.FromSeconds(30);
    private const string LauncherClientHeaderName = "X-BivLauncher-Client";
    private const string LauncherProofHeaderName = "X-BivLauncher-Proof";
    private const string LauncherClientProofMetadataKey = "BivLauncher.ClientProof";
    private const string LauncherApiBaseUrlEnvVar = "BIVLAUNCHER_API_BASE_URL";
    private const string LauncherApiBaseUrlRuEnvVar = "BIVLAUNCHER_API_BASE_URL_RU";
    private const string LauncherApiBaseUrlEuEnvVar = "BIVLAUNCHER_API_BASE_URL_EU";
    private const string LauncherApiBaseUrlAssemblyMetadataKey = "BivLauncher.ApiBaseUrl";
    private const string LauncherApiBaseUrlRuAssemblyMetadataKey = "BivLauncher.ApiBaseUrlRu";
    private const string LauncherApiBaseUrlEuAssemblyMetadataKey = "BivLauncher.ApiBaseUrlEu";
    private const string LauncherFallbackApiBaseUrlsAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls";
    private const string LauncherFallbackApiBaseUrlsRuAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls.Ru";
    private const string LauncherFallbackApiBaseUrlsEuAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls.Eu";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public LauncherApiService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BuildUserAgentValue());
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(LauncherClientHeaderName, BuildClientHeaderValue());

        var launcherProof = ResolveAssemblyMetadata(LauncherClientProofMetadataKey);
        if (!string.IsNullOrWhiteSpace(launcherProof))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(LauncherProofHeaderName, launcherProof);
        }
    }

    public async Task<BootstrapResponse> GetBootstrapAsync(
        string apiBaseUrl,
        string accessToken = "",
        string tokenType = "Bearer",
        CancellationToken cancellationToken = default)
    {
        var cacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var uri = BuildUri(apiBaseUrl, $"/api/public/bootstrap?v={cacheBust}");
        using var response = await SendWithRetryAsync(
            () => BuildOptionalAuthorizedRequest(HttpMethod.Get, uri, accessToken, tokenType),
            cancellationToken,
            maxAttempts: 1,
            attemptTimeout: ManifestRequestAttemptTimeout,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Bootstrap", response, body);
        }

        var payload = JsonSerializer.Deserialize<BootstrapResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Bootstrap response is empty.");
    }

    public async Task<PublicAuthLoginResponse> LoginAsync(
        string apiBaseUrl,
        PublicAuthLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, "/api/public/auth/login");
        using var response = await SendWithRetryAsync(
            () => BuildJsonPostRequest(uri, request),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Login", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicAuthLoginResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Login response is empty.");
    }

    public async Task<PublicAuthSessionResponse> GetSessionAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType = "Bearer",
        CancellationToken cancellationToken = default)
    {
        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Session token is required.");
        }

        var uri = BuildUri(apiBaseUrl, "/api/public/auth/session");
        using var response = await SendWithRetryAsync(
            () => BuildAuthorizedRequest(HttpMethod.Get, uri, token, tokenType),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Session", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicAuthSessionResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Session response is empty.");
    }

    public async Task LogoutAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType = "Bearer",
        CancellationToken cancellationToken = default)
    {
        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var uri = BuildUri(apiBaseUrl, "/api/public/auth/logout");
        using var response = await SendWithRetryAsync(
            () => BuildAuthorizedRequest(HttpMethod.Post, uri, token, tokenType),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        if (response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateApiException("Logout", response, body);
    }

    public async Task<PublicGameSessionStartResponse> StartGameSessionAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType,
        PublicGameSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Session token is required.");
        }

        var uri = BuildUri(apiBaseUrl, "/api/public/auth/game-session/start");
        using var response = await SendWithRetryAsync(
            () => BuildAuthorizedJsonPostRequest(uri, token, tokenType, request),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Game session start", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicGameSessionStartResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Game session start response is empty.");
    }

    public async Task HeartbeatGameSessionAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Session token is required.");
        }

        var uri = BuildUri(apiBaseUrl, "/api/public/auth/game-session/heartbeat");
        using var response = await SendWithRetryAsync(
            () => BuildAuthorizedJsonPostRequest(
                uri,
                token,
                tokenType,
                new PublicGameSessionHeartbeatRequest { SessionId = sessionId }),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateApiException("Game session heartbeat", response, body);
    }

    public async Task StopGameSessionAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var uri = BuildUri(apiBaseUrl, "/api/public/auth/game-session/stop");
        using var response = await SendWithRetryAsync(
            () => BuildAuthorizedJsonPostRequest(
                uri,
                token,
                tokenType,
                new PublicGameSessionStopRequest { SessionId = sessionId }),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateApiException("Game session stop", response, body);
    }

    public async Task<PublicSecurityViolationReportResponse> ReportSecurityViolationAsync(
        string apiBaseUrl,
        string accessToken,
        string tokenType,
        PublicSecurityViolationReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = accessToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Session token is required.");
        }

        var uri = BuildUri(apiBaseUrl, "/api/public/auth/security-violation");
        using var response = await SendWithRetryAsync(
            () => BuildAuthorizedJsonPostRequest(uri, token, tokenType, request),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Security violation report", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicSecurityViolationReportResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Security violation response is empty.");
    }

    public Task<bool> HasSkinAsync(string apiBaseUrl, string username, CancellationToken cancellationToken = default)
    {
        return CheckResourceExistsAsync(apiBaseUrl, $"/api/public/skins/{Uri.EscapeDataString(username)}", cancellationToken);
    }

    public Task<bool> HasCapeAsync(string apiBaseUrl, string username, CancellationToken cancellationToken = default)
    {
        return CheckResourceExistsAsync(apiBaseUrl, $"/api/public/capes/{Uri.EscapeDataString(username)}", cancellationToken);
    }

    public async Task<LauncherManifest> GetManifestAsync(
        string apiBaseUrl,
        string profileSlug,
        string accessToken = "",
        string tokenType = "Bearer",
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, $"/api/public/manifest/{Uri.EscapeDataString(profileSlug)}");
        var body = Encoding.UTF8.GetString(
            await DownloadManifestByRangesAsync(uri, accessToken, tokenType, cancellationToken));
        var payload = JsonSerializer.Deserialize<LauncherManifest>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Manifest response is empty.");
    }

    private async Task<byte[]> DownloadManifestByRangesAsync(
        Uri uri,
        string accessToken,
        string tokenType,
        CancellationToken cancellationToken)
    {
        using var firstResponse = await SendManifestRangeRequestAsync(
            uri,
            accessToken,
            tokenType,
            0,
            ManifestChunkSizeBytes - 1,
            cancellationToken);

        if (!firstResponse.IsSuccessStatusCode)
        {
            var errorBody = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
            throw CreateApiException("Manifest", firstResponse, errorBody);
        }

        if (firstResponse.StatusCode != HttpStatusCode.PartialContent)
        {
            return await ReadHttpContentBytesAsync(
                firstResponse.Content,
                ManifestRequestAttemptTimeout,
                cancellationToken);
        }

        var contentRange = firstResponse.Content.Headers.ContentRange;
        if (contentRange?.Length is not long totalLength || totalLength <= 0)
        {
            throw new InvalidOperationException("Manifest range response did not include total length.");
        }

        var firstChunk = await ReadHttpContentBytesAsync(
            firstResponse.Content,
            ManifestChunkRequestAttemptTimeout,
            cancellationToken);
        using var buffer = new MemoryStream((int)totalLength);
        await buffer.WriteAsync(firstChunk, cancellationToken);

        for (long offset = firstChunk.Length; offset < totalLength; offset += ManifestChunkSizeBytes)
        {
            var end = Math.Min(totalLength - 1, offset + ManifestChunkSizeBytes - 1);
            using var response = await SendManifestRangeRequestAsync(
                uri,
                accessToken,
                tokenType,
                offset,
                end,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw CreateApiException("Manifest", response, errorBody);
            }

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new InvalidOperationException("Manifest range response lost partial content support.");
            }

            var chunk = await ReadHttpContentBytesAsync(
                response.Content,
                ManifestChunkRequestAttemptTimeout,
                cancellationToken);
            await buffer.WriteAsync(chunk, cancellationToken);
        }

        return buffer.ToArray();
    }

    private async Task<HttpResponseMessage> SendManifestRangeRequestAsync(
        Uri uri,
        string accessToken,
        string tokenType,
        long from,
        long to,
        CancellationToken cancellationToken)
    {
        using var request = BuildManifestRangeRequest(uri, accessToken, tokenType, from, to);
        using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptTimeoutCts.CancelAfter(ManifestChunkRequestAttemptTimeout);
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            attemptTimeoutCts.Token);
    }

    private static async Task<byte[]> ReadHttpContentBytesAsync(
        HttpContent content,
        TimeSpan readTimeout,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await CopyStreamWithIdleTimeoutAsync(stream, buffer, readTimeout, cancellationToken);
        return buffer.ToArray();
    }

    public async Task DownloadAssetToFileAsync(
        string apiBaseUrl,
        string s3Key,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var candidateUris = BuildAssetCandidateUris(apiBaseUrl, s3Key);
        Exception? lastError = null;

        foreach (var candidateUri in candidateUris)
        {
            try
            {
                await DownloadAssetCandidateToFileAsync(candidateUri, destinationPath, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
            {
                lastError = ex;
                TryDeleteFile(destinationPath);
            }
        }

        throw lastError ?? new InvalidOperationException("No reachable asset endpoints are configured.");
    }

    private async Task DownloadAssetCandidateToFileAsync(
        Uri candidateUri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            TryDeleteFile(destinationPath);
            try
            {
                await DownloadAssetCandidateToFileCoreAsync(candidateUri, destinationPath, cancellationToken);
                return;
            }
            catch (Exception ex) when (IsTransientAssetDownloadException(ex, cancellationToken) && attempt < MaxRetryAttempts)
            {
                lastError = ex;
                await Task.Delay(ResolveRetryDelay(response: null, attempt), cancellationToken);
            }
        }

        throw lastError ?? new IOException($"Asset download failed for '{candidateUri}'.");
    }

    private async Task DownloadAssetCandidateToFileCoreAsync(
        Uri candidateUri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        EnsureParentDirectory(destinationPath);

        using var firstResponse = await SendAssetRangeRequestAsync(
            candidateUri,
            0,
            AssetChunkSizeBytes - 1,
            cancellationToken);

        if (!firstResponse.IsSuccessStatusCode)
        {
            var errorBody = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
            throw CreateApiException("Asset", firstResponse, errorBody);
        }

        if (firstResponse.StatusCode != HttpStatusCode.PartialContent)
        {
            await DownloadAssetWholeResponseToFileAsync(candidateUri, destinationPath, cancellationToken);
            return;
        }

        var contentRange = firstResponse.Content.Headers.ContentRange;
        if (contentRange?.Length is not long totalLength || totalLength <= 0)
        {
            throw new InvalidOperationException("Asset range response did not include total length.");
        }

        await using var targetStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyHttpContentToStreamAsync(
            firstResponse.Content,
            targetStream,
            AssetChunkRequestAttemptTimeout,
            cancellationToken);

        for (long offset = targetStream.Length; offset < totalLength; offset += AssetChunkSizeBytes)
        {
            var end = Math.Min(totalLength - 1, offset + AssetChunkSizeBytes - 1);
            Exception? lastChunkError = null;
            var chunkCompleted = false;

            for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                targetStream.Position = offset;
                targetStream.SetLength(offset);

                try
                {
                    using var response = await SendAssetRangeRequestAsync(
                        candidateUri,
                        offset,
                        end,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        throw CreateApiException("Asset", response, errorBody);
                    }

                    if (response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new InvalidOperationException("Asset range response lost partial content support.");
                    }

                    await CopyHttpContentToStreamAsync(
                        response.Content,
                        targetStream,
                        AssetChunkRequestAttemptTimeout,
                        cancellationToken);
                    chunkCompleted = true;
                    break;
                }
                catch (Exception ex) when (IsTransientAssetDownloadException(ex, cancellationToken) && attempt < MaxRetryAttempts)
                {
                    lastChunkError = ex;
                    await Task.Delay(ResolveRetryDelay(response: null, attempt), cancellationToken);
                }
            }

            if (!chunkCompleted)
            {
                throw lastChunkError ?? new IOException($"Asset chunk download failed for '{candidateUri}'.");
            }
        }

        await targetStream.FlushAsync(cancellationToken);
        if (targetStream.Length != totalLength)
        {
            throw new IOException(
                $"Asset download size mismatch for '{candidateUri}'. Expected {totalLength} bytes, received {targetStream.Length}.");
        }
    }

    private async Task DownloadAssetWholeResponseToFileAsync(
        Uri candidateUri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            TryDeleteFile(destinationPath);
            try
            {
                using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeoutCts.CancelAfter(ManifestRequestAttemptTimeout);
                using var response = await _httpClient.GetAsync(
                    candidateUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptTimeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw CreateApiException("Asset", response, errorBody);
                }

                await using var targetStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await CopyHttpContentToStreamAsync(
                    response.Content,
                    targetStream,
                    ManifestRequestAttemptTimeout,
                    cancellationToken);
                await targetStream.FlushAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (IsTransientAssetDownloadException(ex, cancellationToken) && attempt < MaxRetryAttempts)
            {
                lastError = ex;
                await Task.Delay(ResolveRetryDelay(response: null, attempt), cancellationToken);
            }
        }

        throw lastError ?? new IOException($"Asset download failed for '{candidateUri}'.");
    }

    private async Task<HttpResponseMessage> SendAssetRangeRequestAsync(
        Uri uri,
        long from,
        long to,
        CancellationToken cancellationToken)
    {
        using var request = BuildAssetRangeRequest(uri, from, to);
        using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptTimeoutCts.CancelAfter(AssetChunkRequestAttemptTimeout);
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            attemptTimeoutCts.Token);
    }

    private static async Task CopyHttpContentToStreamAsync(
        HttpContent content,
        Stream target,
        TimeSpan readTimeout,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await CopyStreamWithIdleTimeoutAsync(source, target, readTimeout, cancellationToken);
    }

    private static async Task CopyStreamWithIdleTimeoutAsync(
        Stream source,
        Stream target,
        TimeSpan idleReadTimeout,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeoutCts.CancelAfter(idleReadTimeout);
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), readTimeoutCts.Token);
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsTransientAssetDownloadException(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is TaskCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        return ex is HttpRequestException or IOException;
    }

    private static void EnsureParentDirectory(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public async Task<Stream> OpenAssetReadStreamAsync(string apiBaseUrl, string s3Key, CancellationToken cancellationToken = default)
    {
        var candidateUris = BuildAssetCandidateUris(apiBaseUrl, s3Key);
        Exception? lastError = null;

        foreach (var candidateUri in candidateUris)
        {
            try
            {
                using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeoutCts.CancelAfter(ApiRequestAttemptTimeout);
                var response = await _httpClient.GetAsync(
                    candidateUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptTimeoutCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return new HttpResponseStream(stream, response);
                }

                if (!ShouldTryNextAssetLocation(response.StatusCode))
                {
                    response.EnsureSuccessStatusCode();
                }

                lastError = new HttpRequestException(
                    $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    null,
                    response.StatusCode);
                response.Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("No reachable asset endpoints are configured.");
    }

    private static IReadOnlyList<Uri> BuildAssetCandidateUris(string apiBaseUrl, string? assetReference)
    {
        var candidates = new List<Uri>();

        void AddAbsoluteUri(Uri? candidateUri)
        {
            if (candidateUri is null || !candidateUri.IsAbsoluteUri)
            {
                return;
            }

            if (candidates.Any(existing => Uri.Compare(existing, candidateUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0))
            {
                return;
            }

            candidates.Add(candidateUri);
        }

        var normalizedReference = (assetReference ?? string.Empty).Trim();
        if (TryResolvePublicAssetPath(normalizedReference, out var publicAssetPath))
        {
            foreach (var apiBaseUrlCandidate in ResolveAssetApiBaseUrlCandidates(apiBaseUrl))
            {
                AddAbsoluteUri(BuildUri(apiBaseUrlCandidate, publicAssetPath));
            }

            return candidates;
        }

        if (Uri.TryCreate(normalizedReference, UriKind.Absolute, out var absoluteUri))
        {
            AddAbsoluteUri(absoluteUri);
            return candidates;
        }

        var escaped = string.Join('/',
            normalizedReference
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        var builtPath = $"/api/public/assets/{escaped}";
        foreach (var apiBaseUrlCandidate in ResolveAssetApiBaseUrlCandidates(apiBaseUrl))
        {
            AddAbsoluteUri(BuildUri(apiBaseUrlCandidate, builtPath));
        }

        return candidates;
    }

    private static IEnumerable<string> ResolveAssetApiBaseUrlCandidates(string apiBaseUrl)
    {
        var candidates = new List<string>();

        void Add(string? value)
        {
            var normalized = NormalizeBaseUrlOrEmpty(value);
            if (string.IsNullOrWhiteSpace(normalized) ||
                candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            candidates.Add(normalized);
        }

        Add(apiBaseUrl);
        var selectedRegionCode = ResolveRequestedRegionCode(apiBaseUrl);
        if (!string.IsNullOrWhiteSpace(selectedRegionCode))
        {
            Add(ResolveRegionalApiBaseUrl(selectedRegionCode));

            foreach (var bundledFallback in ResolveBundledFallbackApiBaseUrls(selectedRegionCode))
            {
                Add(bundledFallback);
            }
        }
        else
        {
            Add(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEnvVar));
            Add(Environment.GetEnvironmentVariable(LauncherApiBaseUrlRuEnvVar));
            Add(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEuEnvVar));
            Add(ResolveAssemblyMetadata(LauncherApiBaseUrlAssemblyMetadataKey));
            Add(ResolveAssemblyMetadata(LauncherApiBaseUrlRuAssemblyMetadataKey));
            Add(ResolveAssemblyMetadata(LauncherApiBaseUrlEuAssemblyMetadataKey));

            foreach (var bundledFallback in ResolveBundledFallbackApiBaseUrls())
            {
                Add(bundledFallback);
            }
        }

        return candidates;
    }

    private static string ResolveRequestedRegionCode(string? apiBaseUrl)
    {
        var normalizedApiBaseUrl = NormalizeBaseUrlOrEmpty(apiBaseUrl);
        if (string.IsNullOrWhiteSpace(normalizedApiBaseUrl))
        {
            return string.Empty;
        }

        if (BaseUrlsEqual(normalizedApiBaseUrl, ResolveRegionalApiBaseUrl("ru")) ||
            ResolveBundledFallbackApiBaseUrls("ru").Contains(normalizedApiBaseUrl, StringComparer.OrdinalIgnoreCase))
        {
            return "ru";
        }

        if (BaseUrlsEqual(normalizedApiBaseUrl, ResolveRegionalApiBaseUrl("eu")) ||
            ResolveBundledFallbackApiBaseUrls("eu").Contains(normalizedApiBaseUrl, StringComparer.OrdinalIgnoreCase))
        {
            return "eu";
        }

        return string.Empty;
    }

    private static bool TryResolvePublicAssetPath(string assetReference, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(assetReference))
        {
            return false;
        }

        if (Uri.TryCreate(assetReference, UriKind.Absolute, out var absoluteUri))
        {
            if (!absoluteUri.AbsolutePath.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = $"{absoluteUri.AbsolutePath}{absoluteUri.Query}";
            return true;
        }

        if (!assetReference.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = assetReference;
        return true;
    }

    private static bool ShouldTryNextAssetLocation(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.NotFound or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    public async Task<PublicCrashReportCreateResponse> SubmitCrashReportAsync(
        string apiBaseUrl,
        PublicCrashReportCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, "/api/public/crashes");
        using var response = await SendWithRetryAsync(
            () => BuildJsonPostRequest(uri, request),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Crash report", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicCrashReportCreateResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Crash report response is empty.");
    }

    public async Task<PublicInstallTelemetryTrackResponse> SubmitInstallTelemetryAsync(
        string apiBaseUrl,
        PublicInstallTelemetryTrackRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, "/api/public/install-telemetry");
        using var response = await SendWithRetryAsync(
            () => BuildJsonPostRequest(uri, request),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Install telemetry", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicInstallTelemetryTrackResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Install telemetry response is empty.");
    }

    private static Uri BuildUri(string apiBaseUrl, string path)
    {
        var baseUrl = apiBaseUrl.Trim().TrimEnd('/');
        return new Uri($"{baseUrl}{path}");
    }

    private async Task<bool> CheckResourceExistsAsync(string apiBaseUrl, string path, CancellationToken cancellationToken)
    {
        var uri = BuildUri(apiBaseUrl, path);
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken,
            completionOption: HttpCompletionOption.ResponseContentRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateApiException("Resource check", response, body);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        int maxAttempts = MaxRetryAttempts,
        TimeSpan? attemptTimeout = null,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var effectiveMaxAttempts = Math.Max(1, maxAttempts);
        var effectiveAttemptTimeout = attemptTimeout ?? ApiRequestAttemptTimeout;

        for (var attempt = 1; attempt <= effectiveMaxAttempts; attempt++)
        {
            try
            {
                using var request = requestFactory();
                using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeoutCts.CancelAfter(effectiveAttemptTimeout);
                var response = await _httpClient.SendAsync(
                    request,
                    completionOption,
                    attemptTimeoutCts.Token);

                if (attempt >= effectiveMaxAttempts || !ShouldRetry(response.StatusCode))
                {
                    return response;
                }

                var delay = ResolveRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientSendException(ex, cancellationToken) && attempt < effectiveMaxAttempts)
            {
                var delay = ResolveRetryDelay(response: null, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("HTTP request failed after retries.");
    }

    private static HttpRequestMessage BuildJsonPostRequest<T>(Uri uri, T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpRequestMessage BuildAuthorizedJsonPostRequest<T>(
        Uri uri,
        string accessToken,
        string tokenType,
        T payload)
    {
        var request = BuildJsonPostRequest(uri, payload);
        var normalizedType = string.IsNullOrWhiteSpace(tokenType) ? "Bearer" : tokenType.Trim();
        request.Headers.Authorization = new AuthenticationHeaderValue(normalizedType, accessToken.Trim());
        return request;
    }

    private static HttpRequestMessage BuildAuthorizedRequest(HttpMethod method, Uri uri, string accessToken, string tokenType)
    {
        var request = new HttpRequestMessage(method, uri);
        var normalizedType = string.IsNullOrWhiteSpace(tokenType) ? "Bearer" : tokenType.Trim();
        request.Headers.Authorization = new AuthenticationHeaderValue(normalizedType, accessToken.Trim());
        return request;
    }

    private static HttpRequestMessage BuildOptionalAuthorizedRequest(
        HttpMethod method,
        Uri uri,
        string? accessToken,
        string tokenType)
    {
        var token = (accessToken ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(token)
            ? new HttpRequestMessage(method, uri)
            : BuildAuthorizedRequest(method, uri, token, tokenType);
    }

    private static HttpRequestMessage BuildManifestRangeRequest(
        Uri uri,
        string? accessToken,
        string tokenType,
        long from,
        long to)
    {
        var request = BuildOptionalAuthorizedRequest(HttpMethod.Get, uri, accessToken, tokenType);
        request.Headers.Range = new RangeHeaderValue(from, to);
        request.Headers.ConnectionClose = true;
        request.Headers.AcceptEncoding.Clear();
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        return request;
    }

    private static HttpRequestMessage BuildAssetRangeRequest(Uri uri, long from, long to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(from, to);
        request.Headers.ConnectionClose = true;
        request.Headers.AcceptEncoding.Clear();
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        return request;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientSendException(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is TaskCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        return ex is HttpRequestException;
    }

    private static TimeSpan ResolveRetryDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfterDelay = response?.Headers.RetryAfter?.Delta;
        if (retryAfterDelay.HasValue && retryAfterDelay.Value > TimeSpan.Zero)
        {
            return ClampDelay(retryAfterDelay.Value);
        }

        if (response?.Headers.RetryAfter?.Date is DateTimeOffset retryAfterDate)
        {
            var delta = retryAfterDate - DateTimeOffset.UtcNow;
            if (delta > TimeSpan.Zero)
            {
                return ClampDelay(delta);
            }
        }

        var backoffMs = Math.Min(5000, 450 * (1 << (attempt - 1)));
        return TimeSpan.FromMilliseconds(backoffMs);
    }

    private static TimeSpan ClampDelay(TimeSpan delay)
    {
        var min = TimeSpan.FromMilliseconds(250);
        var max = TimeSpan.FromSeconds(15);
        if (delay < min)
        {
            return min;
        }

        if (delay > max)
        {
            return max;
        }

        return delay;
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString();
            }

            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ExtractErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
            {
                return codeElement.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string BuildErrorMessage(string operation, HttpResponseMessage response, string body)
    {
        var explicitError = ExtractError(body);
        if (!string.IsNullOrWhiteSpace(explicitError))
        {
            return explicitError;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return $"{operation} is temporarily unavailable.";
        }

        return $"{operation} failed with status {(int)response.StatusCode}.";
    }

    private static LauncherApiException CreateApiException(string operation, HttpResponseMessage response, string body)
    {
        var message = BuildErrorMessage(operation, response, body);
        var errorCode = ExtractErrorCode(body) ?? string.Empty;
        return new LauncherApiException(message, response.StatusCode, ResolveRetryAfter(response), errorCode);
    }

    private static TimeSpan? ResolveRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset retryAfterDate)
        {
            var calculated = retryAfterDate - DateTimeOffset.UtcNow;
            if (calculated > TimeSpan.Zero)
            {
                return calculated;
            }
        }

        return null;
    }

    private static string BuildUserAgentValue()
    {
        var version = ResolveLauncherVersion();
        return $"BivLauncher.Client/{version}";
    }

    private static string BuildClientHeaderValue()
    {
        var version = ResolveLauncherVersion();
        return $"BivLauncher.Client/{version}";
    }

    private static string ResolveLauncherVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion is null
            ? "0.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }

    private static string ResolveAssemblyMetadata(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        return (attribute?.Value ?? string.Empty).Trim();
    }

    private static string ResolveRegionalApiBaseUrl(string regionCode)
    {
        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        if (string.IsNullOrWhiteSpace(normalizedRegionCode))
        {
            return string.Empty;
        }

        return normalizedRegionCode switch
        {
            "ru" => NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable(LauncherApiBaseUrlRuEnvVar))
                is var envRu && !string.IsNullOrWhiteSpace(envRu)
                    ? envRu
                    : NormalizeBaseUrlOrEmpty(ResolveAssemblyMetadata(LauncherApiBaseUrlRuAssemblyMetadataKey)),
            "eu" => NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEuEnvVar))
                is var envEu && !string.IsNullOrWhiteSpace(envEu)
                    ? envEu
                    : NormalizeBaseUrlOrEmpty(ResolveAssemblyMetadata(LauncherApiBaseUrlEuAssemblyMetadataKey)),
            _ => string.Empty
        };
    }

    private static string NormalizeApiRegionCode(string? regionCode)
    {
        return (regionCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ru" => "ru",
            "eu" => "eu",
            _ => string.Empty
        };
    }

    private static bool BaseUrlsEqual(string? left, string? right)
    {
        return string.Equals(
            NormalizeBaseUrlOrEmpty(left),
            NormalizeBaseUrlOrEmpty(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveBundledFallbackApiBaseUrls(string? regionCode = null)
    {
        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        var metadataKey = normalizedRegionCode switch
        {
            "ru" => LauncherFallbackApiBaseUrlsRuAssemblyMetadataKey,
            "eu" => LauncherFallbackApiBaseUrlsEuAssemblyMetadataKey,
            _ => LauncherFallbackApiBaseUrlsAssemblyMetadataKey
        };
        var rawValue = ResolveAssemblyMetadata(metadataKey);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeBaseUrlOrEmpty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeBaseUrlOrEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('/');
    }

    private sealed class HttpResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        private readonly Stream _inner = inner;
        private readonly HttpResponseMessage _response = response;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask DisposeAsync()
        {
            _response.Dispose();
            return _inner.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
