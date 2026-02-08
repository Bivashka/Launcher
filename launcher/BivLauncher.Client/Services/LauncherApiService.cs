using BivLauncher.Client.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class LauncherApiService : ILauncherApiService
{
    private const int MaxRetryAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public async Task<BootstrapResponse> GetBootstrapAsync(string apiBaseUrl, CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, "/api/public/bootstrap");
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken);
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
            cancellationToken);
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
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Session", response, body);
        }

        var payload = JsonSerializer.Deserialize<PublicAuthSessionResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Session response is empty.");
    }

    public Task<bool> HasSkinAsync(string apiBaseUrl, string username, CancellationToken cancellationToken = default)
    {
        return CheckResourceExistsAsync(apiBaseUrl, $"/api/public/skins/{Uri.EscapeDataString(username)}", cancellationToken);
    }

    public Task<bool> HasCapeAsync(string apiBaseUrl, string username, CancellationToken cancellationToken = default)
    {
        return CheckResourceExistsAsync(apiBaseUrl, $"/api/public/capes/{Uri.EscapeDataString(username)}", cancellationToken);
    }

    public async Task<LauncherManifest> GetManifestAsync(string apiBaseUrl, string profileSlug, CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, $"/api/public/manifest/{Uri.EscapeDataString(profileSlug)}");
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("Manifest", response, body);
        }

        var payload = JsonSerializer.Deserialize<LauncherManifest>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Manifest response is empty.");
    }

    public async Task<Stream> OpenAssetReadStreamAsync(string apiBaseUrl, string s3Key, CancellationToken cancellationToken = default)
    {
        var escaped = string.Join('/',
            s3Key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var uri = BuildUri(apiBaseUrl, $"/api/public/assets/{escaped}");

        var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task<PublicCrashReportCreateResponse> SubmitCrashReportAsync(
        string apiBaseUrl,
        PublicCrashReportCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, "/api/public/crashes");
        using var response = await SendWithRetryAsync(
            () => BuildJsonPostRequest(uri, request),
            cancellationToken);
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
            cancellationToken);
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
            cancellationToken);
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
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                using var request = requestFactory();
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (attempt >= MaxRetryAttempts || !ShouldRetry(response.StatusCode))
                {
                    return response;
                }

                var delay = ResolveRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientSendException(ex, cancellationToken) && attempt < MaxRetryAttempts)
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

    private static HttpRequestMessage BuildAuthorizedRequest(HttpMethod method, Uri uri, string accessToken, string tokenType)
    {
        var request = new HttpRequestMessage(method, uri);
        var normalizedType = string.IsNullOrWhiteSpace(tokenType) ? "Bearer" : tokenType.Trim();
        request.Headers.Authorization = new AuthenticationHeaderValue(normalizedType, accessToken.Trim());
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

    private static string BuildErrorMessage(string operation, HttpResponseMessage response, string body)
    {
        var explicitError = ExtractError(body);
        if (!string.IsNullOrWhiteSpace(explicitError))
        {
            return explicitError;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfterDelta && retryAfterDelta > TimeSpan.Zero)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling(retryAfterDelta.TotalSeconds));
                return $"{operation} is temporarily rate-limited. Retry in {seconds} second(s).";
            }

            return $"{operation} is temporarily rate-limited. Retry shortly.";
        }

        return $"{operation} failed with status {(int)response.StatusCode}.";
    }

    private static LauncherApiException CreateApiException(string operation, HttpResponseMessage response, string body)
    {
        var message = BuildErrorMessage(operation, response, body);
        return new LauncherApiException(message, response.StatusCode, ResolveRetryAfter(response));
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
}
