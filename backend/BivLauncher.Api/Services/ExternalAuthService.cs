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
        if (string.Equals(settings.AuthMode, "any", StringComparison.OrdinalIgnoreCase))
        {
            return CreateAnyModeSuccess(username);
        }

        var loginUrl = settings.LoginUrl.Trim();
        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            if (settings.AllowDevFallback)
            {
                return CreateAnyModeSuccess(username);
            }

            return new ExternalAuthResult
            {
                Success = false,
                ErrorMessage = "Auth provider login URL is not configured."
            };
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120));

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [settings.LoginFieldKey] = username,
            [settings.PasswordFieldKey] = password
        };
        if (!string.IsNullOrWhiteSpace(hwidHash))
        {
            payload["hwidHash"] = hwidHash;
        }

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
                AuthMode = NormalizeAuthMode(_options.AuthMode),
                LoginUrl = _options.LoginUrl,
                LoginFieldKey = NormalizeFieldKey(_options.LoginFieldKey, "username"),
                PasswordFieldKey = NormalizeFieldKey(_options.PasswordFieldKey, "password"),
                TimeoutSeconds = Math.Clamp(_options.TimeoutSeconds, 5, 120),
                AllowDevFallback = _options.AllowDevFallback
            };
        }

        return new AuthProviderOptions
        {
            AuthMode = NormalizeAuthMode(stored.AuthMode),
            LoginUrl = stored.LoginUrl,
            LoginFieldKey = NormalizeFieldKey(stored.LoginFieldKey, "username"),
            PasswordFieldKey = NormalizeFieldKey(stored.PasswordFieldKey, "password"),
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
                Success = false,
                ErrorMessage = "Auth provider returned empty response."
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = "Auth provider returned invalid JSON payload."
                };
            }

            var hasExplicitSuccess = TryGetBoolean(root, out var explicitSuccess, "success", "ok");
            if (hasExplicitSuccess && !explicitSuccess)
            {
                return new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = TryExtractError(json) ?? "Authentication failed."
                };
            }

            var externalId = GetString(root, "externalId", "userId", "id");
            var username = GetString(root, "username", "name");
            if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(username))
            {
                return new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = hasExplicitSuccess && explicitSuccess
                        ? "Auth provider returned success without identity fields."
                        : TryExtractError(json) ?? "Auth provider returned invalid auth response."
                };
            }

            var resolvedExternalId = externalId ?? username ?? fallbackUsername;
            var resolvedUsername = username ?? externalId ?? fallbackUsername;
            var roles = GetRoles(root);
            var banned = TryGetBoolean(root, out var bannedValue, "banned") && bannedValue;

            return new ExternalAuthResult
            {
                Success = true,
                ExternalId = resolvedExternalId,
                Username = resolvedUsername,
                Roles = roles.Count == 0 ? ["player"] : roles,
                Banned = banned
            };
        }
        catch (JsonException)
        {
            return new ExternalAuthResult
            {
                Success = false,
                ErrorMessage = "Auth provider returned invalid JSON."
            };
        }
    }

    private static List<string> GetRoles(JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "roles", out var rolesElement))
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

    private static string? GetString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(root, propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = value.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
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
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return GetString(root, "error", "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExternalAuthResult CreateAnyModeSuccess(string username)
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

    private static string NormalizeAuthMode(string? authMode)
    {
        return string.Equals(authMode?.Trim(), "any", StringComparison.OrdinalIgnoreCase)
            ? "any"
            : "external";
    }

    private static string NormalizeFieldKey(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var value = raw.Trim();
        if (value.Length > 64)
        {
            return fallback;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            var isValid =
                (ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch is '_' or '-' or '.';
            if (!isValid)
            {
                return fallback;
            }
        }

        return value;
    }

    private static bool TryGetBoolean(JsonElement root, out bool value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(root, propertyName, out var propertyValue))
            {
                continue;
            }

            switch (propertyValue.ValueKind)
            {
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    value = false;
                    return true;
                case JsonValueKind.Number when propertyValue.TryGetInt32(out var number):
                    value = number != 0;
                    return true;
                case JsonValueKind.String:
                    var raw = propertyValue.GetString();
                    if (bool.TryParse(raw, out var parsedBool))
                    {
                        value = parsedBool;
                        return true;
                    }

                    if (int.TryParse(raw, out var parsedNumber))
                    {
                        value = parsedNumber != 0;
                        return true;
                    }

                    break;
            }
        }

        value = false;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
