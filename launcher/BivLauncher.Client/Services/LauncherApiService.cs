using BivLauncher.Client.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class LauncherApiService : ILauncherApiService
{
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
        var response = await _httpClient.GetFromJsonAsync<BootstrapResponse>(uri, JsonOptions, cancellationToken);
        return response ?? throw new InvalidOperationException("Bootstrap response is empty.");
    }

    public async Task<PublicAuthLoginResponse> LoginAsync(
        string apiBaseUrl,
        PublicAuthLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(apiBaseUrl, "/api/public/auth/login");
        using var response = await _httpClient.PostAsJsonAsync(uri, request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body) ?? $"Login failed with status {(int)response.StatusCode}.");
        }

        var payload = JsonSerializer.Deserialize<PublicAuthLoginResponse>(body, JsonOptions);
        return payload ?? throw new InvalidOperationException("Login response is empty.");
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
        var response = await _httpClient.GetFromJsonAsync<LauncherManifest>(uri, JsonOptions, cancellationToken);
        return response ?? throw new InvalidOperationException("Manifest response is empty.");
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

    private static Uri BuildUri(string apiBaseUrl, string path)
    {
        var baseUrl = apiBaseUrl.Trim().TrimEnd('/');
        return new Uri($"{baseUrl}{path}");
    }

    private async Task<bool> CheckResourceExistsAsync(string apiBaseUrl, string path, CancellationToken cancellationToken)
    {
        var uri = BuildUri(apiBaseUrl, path);
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode;
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
}
