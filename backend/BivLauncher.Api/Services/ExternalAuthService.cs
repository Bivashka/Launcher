using BivLauncher.Api.Options;
using BivLauncher.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class ExternalAuthService(
    AppDbContext dbContext,
    IOptions<AuthProviderOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<ExternalAuthService> logger) : IExternalAuthService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly AuthProviderOptions _options = options.Value;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<ExternalAuthService> _logger = logger;

    public async Task<ExternalAuthResult> AuthenticateAsync(
        string username,
        string password,
        string hwidHash,
        CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var loginUrl = settings.LoginUrl.Trim();
        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            if (settings.AllowDevFallback)
            {
                return new ExternalAuthResult
                {
                    Success = true,
                    ExternalId = username,
                    Username = username,
                    Roles = ["player"],
                    Banned = false
                };
            }

            return new ExternalAuthResult
            {
                Success = false,
                ErrorMessage = "Auth provider login URL is not configured."
            };
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120));

        var payload = new
        {
            username,
            password,
            hwidHash
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(loginUrl, payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = TryExtractError(body) ?? $"Auth provider returned {(int)response.StatusCode}";
                return new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = reason
                };
            }

            return ParseSuccess(username, body);
        }
        catch (TaskCanceledException)
        {
            return new ExternalAuthResult
            {
                Success = false,
                ErrorMessage = "Auth provider timeout."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auth provider call failed.");
            return new ExternalAuthResult
            {
                Success = false,
                ErrorMessage = "Auth provider call failed."
            };
        }
    }

    private async Task<AuthProviderOptions> ResolveSettingsAsync(CancellationToken cancellationToken)
    {
        var stored = await _dbContext.AuthProviderConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return new AuthProviderOptions
            {
                LoginUrl = _options.LoginUrl,
                TimeoutSeconds = Math.Clamp(_options.TimeoutSeconds, 5, 120),
                AllowDevFallback = _options.AllowDevFallback
            };
        }

        return new AuthProviderOptions
        {
            LoginUrl = stored.LoginUrl,
            TimeoutSeconds = Math.Clamp(stored.TimeoutSeconds, 5, 120),
            AllowDevFallback = stored.AllowDevFallback
        };
    }

    private static ExternalAuthResult ParseSuccess(string fallbackUsername, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ExternalAuthResult
            {
                Success = true,
                ExternalId = fallbackUsername,
                Username = fallbackUsername,
                Roles = ["player"]
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var explicitSuccess = root.TryGetProperty("success", out var successElement)
                ? successElement.ValueKind == JsonValueKind.True
                : root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;

            if ((root.TryGetProperty("success", out _) || root.TryGetProperty("ok", out _)) && !explicitSuccess)
            {
                return new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = TryExtractError(json) ?? "Authentication failed."
                };
            }

            var externalId = GetString(root, "externalId")
                ?? GetString(root, "userId")
                ?? GetString(root, "id")
                ?? fallbackUsername;
            var username = GetString(root, "username")
                ?? GetString(root, "name")
                ?? fallbackUsername;
            var roles = GetRoles(root);
            var banned = root.TryGetProperty("banned", out var bannedElement) && bannedElement.ValueKind == JsonValueKind.True;

            return new ExternalAuthResult
            {
                Success = true,
                ExternalId = externalId,
                Username = username,
                Roles = roles.Count == 0 ? ["player"] : roles,
                Banned = banned
            };
        }
        catch
        {
            return new ExternalAuthResult
            {
                Success = true,
                ExternalId = fallbackUsername,
                Username = fallbackUsername,
                Roles = ["player"]
            };
        }
    }

    private static List<string> GetRoles(JsonElement root)
    {
        if (root.TryGetProperty("roles", out var rolesElement))
        {
            if (rolesElement.ValueKind == JsonValueKind.Array)
            {
                return rolesElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (rolesElement.ValueKind == JsonValueKind.String)
            {
                return rolesElement.GetString()!
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return [];
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        return null;
    }

    private static string? TryExtractError(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return GetString(root, "error") ?? GetString(root, "message");
        }
        catch
        {
            return null;
        }
    }
}
