using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/auth-provider")]
public sealed class AdminAuthProviderSettingsController(
    AppDbContext dbContext,
    IOptions<AuthProviderOptions> fallbackOptions,
    IHttpClientFactory httpClientFactory,
    IAdminAuditService auditService) : ControllerBase
{
    private static readonly HashSet<string> SupportedAuthModes = ["external", "any"];

    [HttpGet]
    public async Task<ActionResult<AuthProviderSettingsDto>> Get(CancellationToken cancellationToken)
    {
        return Ok(await ResolveEffectiveSettingsAsync(cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<AuthProviderSettingsDto>> Put(
        [FromBody] AuthProviderSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.AuthProviderConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new AuthProviderConfig();
            dbContext.AuthProviderConfigs.Add(config);
        }

        config.AuthMode = NormalizeAuthMode(request.AuthMode);
        config.LoginUrl = request.LoginUrl.Trim();
        config.LoginFieldKey = NormalizeFieldKey(request.LoginFieldKey, "username");
        config.PasswordFieldKey = NormalizeFieldKey(request.PasswordFieldKey, "password");
        config.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 5, 120);
        config.AllowDevFallback = request.AllowDevFallback;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.auth-provider.update",
            actor: actor,
            entityType: "settings",
            entityId: "auth-provider",
            details: new
            {
                config.AuthMode,
                config.LoginUrl,
                config.LoginFieldKey,
                config.PasswordFieldKey,
                config.TimeoutSeconds,
                config.AllowDevFallback
            },
            cancellationToken: cancellationToken);

        return Ok(new AuthProviderSettingsDto(
            config.AuthMode,
            config.LoginUrl,
            config.LoginFieldKey,
            config.PasswordFieldKey,
            config.TimeoutSeconds,
            config.AllowDevFallback,
            config.UpdatedAtUtc));
    }

    [HttpPost("probe")]
    public async Task<ActionResult<AuthProviderProbeResultDto>> Probe(CancellationToken cancellationToken)
    {
        var settings = await ResolveEffectiveSettingsAsync(cancellationToken);
        var now = DateTime.UtcNow;

        if (string.Equals(settings.AuthMode, "any", StringComparison.OrdinalIgnoreCase))
        {
            var anyResult = new AuthProviderProbeResultDto(
                Success: true,
                AuthMode: settings.AuthMode,
                LoginUrl: settings.LoginUrl,
                StatusCode: null,
                Message: "ANY mode does not require upstream auth endpoint.",
                CheckedAtUtc: now);

            await WriteProbeAuditAsync(anyResult, cancellationToken);
            return Ok(anyResult);
        }

        if (string.IsNullOrWhiteSpace(settings.LoginUrl))
        {
            var emptyResult = new AuthProviderProbeResultDto(
                Success: false,
                AuthMode: settings.AuthMode,
                LoginUrl: settings.LoginUrl,
                StatusCode: null,
                Message: "External auth mode requires Login URL.",
                CheckedAtUtc: now);

            await WriteProbeAuditAsync(emptyResult, cancellationToken);
            return Ok(emptyResult);
        }

        if (!Uri.TryCreate(settings.LoginUrl, UriKind.Absolute, out var loginUri) ||
            (loginUri.Scheme != Uri.UriSchemeHttp && loginUri.Scheme != Uri.UriSchemeHttps))
        {
            var invalidUrlResult = new AuthProviderProbeResultDto(
                Success: false,
                AuthMode: settings.AuthMode,
                LoginUrl: settings.LoginUrl,
                StatusCode: null,
                Message: "Login URL must be an absolute http/https URL.",
                CheckedAtUtc: now);

            await WriteProbeAuditAsync(invalidUrlResult, cancellationToken);
            return Ok(invalidUrlResult);
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120));

            using var request = new HttpRequestMessage(HttpMethod.Get, loginUri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var success = (int)response.StatusCode < 500;
            var message = success
                ? $"Endpoint reachable: HTTP {(int)response.StatusCode}."
                : $"Endpoint returned server error: HTTP {(int)response.StatusCode}.";

            var result = new AuthProviderProbeResultDto(
                Success: success,
                AuthMode: settings.AuthMode,
                LoginUrl: settings.LoginUrl,
                StatusCode: (int)response.StatusCode,
                Message: message,
                CheckedAtUtc: now);

            await WriteProbeAuditAsync(result, cancellationToken);
            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            var timeoutResult = new AuthProviderProbeResultDto(
                Success: false,
                AuthMode: settings.AuthMode,
                LoginUrl: settings.LoginUrl,
                StatusCode: null,
                Message: "Probe request timeout.",
                CheckedAtUtc: now);

            await WriteProbeAuditAsync(timeoutResult, cancellationToken);
            return Ok(timeoutResult);
        }
        catch (HttpRequestException ex)
        {
            var netResult = new AuthProviderProbeResultDto(
                Success: false,
                AuthMode: settings.AuthMode,
                LoginUrl: settings.LoginUrl,
                StatusCode: null,
                Message: $"Network error: {ex.Message}",
                CheckedAtUtc: now);

            await WriteProbeAuditAsync(netResult, cancellationToken);
            return Ok(netResult);
        }
    }

    private async Task<AuthProviderSettingsDto> ResolveEffectiveSettingsAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.AuthProviderConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return new AuthProviderSettingsDto(
                NormalizeAuthMode(fallback.AuthMode),
                fallback.LoginUrl,
                NormalizeFieldKey(fallback.LoginFieldKey, "username"),
                NormalizeFieldKey(fallback.PasswordFieldKey, "password"),
                Math.Clamp(fallback.TimeoutSeconds, 5, 120),
                fallback.AllowDevFallback,
                null);
        }

        return new AuthProviderSettingsDto(
            NormalizeAuthMode(stored.AuthMode),
            stored.LoginUrl,
            NormalizeFieldKey(stored.LoginFieldKey, "username"),
            NormalizeFieldKey(stored.PasswordFieldKey, "password"),
            Math.Clamp(stored.TimeoutSeconds, 5, 120),
            stored.AllowDevFallback,
            stored.UpdatedAtUtc);
    }

    private async Task WriteProbeAuditAsync(AuthProviderProbeResultDto result, CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.auth-provider.probe",
            actor: actor,
            entityType: "settings",
            entityId: "auth-provider",
            details: new
            {
                result.Success,
                result.AuthMode,
                result.LoginUrl,
                result.StatusCode,
                result.Message,
                result.CheckedAtUtc
            },
            cancellationToken: cancellationToken);
    }

    private static string NormalizeAuthMode(string? authMode)
    {
        var normalized = (authMode ?? string.Empty).Trim().ToLowerInvariant();
        return SupportedAuthModes.Contains(normalized) ? normalized : "external";
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
}
