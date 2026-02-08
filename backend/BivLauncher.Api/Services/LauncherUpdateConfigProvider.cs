using BivLauncher.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class LauncherUpdateConfigProvider(
    IWebHostEnvironment environment,
    IOptions<LauncherUpdateOptions> options,
    ILogger<LauncherUpdateConfigProvider> logger) : ILauncherUpdateConfigProvider
{
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IWebHostEnvironment _environment = environment;
    private readonly LauncherUpdateOptions _options = options.Value;
    private readonly ILogger<LauncherUpdateConfigProvider> _logger = logger;

    public async Task<LauncherUpdateConfig?> GetAsync(CancellationToken cancellationToken = default)
    {
        var path = GetConfigPath();
        if (File.Exists(path))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var loaded = await JsonSerializer.DeserializeAsync<LauncherUpdateConfig>(stream, ReadJsonOptions, cancellationToken);
                var normalizedFileConfig = Normalize(loaded);
                if (normalizedFileConfig is not null)
                {
                    return normalizedFileConfig;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read launcher update config file {Path}. Falling back to configured defaults.", path);
            }
        }

        var fallback = new LauncherUpdateConfig(
            LatestVersion: _options.LatestVersion,
            DownloadUrl: _options.DownloadUrl,
            ReleaseNotes: _options.ReleaseNotes);
        return Normalize(fallback);
    }

    public async Task<LauncherUpdateConfig> SaveAsync(LauncherUpdateConfig config, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(config) ?? throw new InvalidOperationException("LatestVersion and DownloadUrl are required.");
        var path = GetConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, normalized, WriteJsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return normalized;
    }

    private string GetConfigPath()
    {
        var configuredPath = _options.FilePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "launcher-update.json";
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_environment.ContentRootPath, configuredPath);
    }

    private static LauncherUpdateConfig? Normalize(LauncherUpdateConfig? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var latestVersion = (raw.LatestVersion ?? string.Empty).Trim();
        var downloadUrl = (raw.DownloadUrl ?? string.Empty).Trim();
        var releaseNotes = (raw.ReleaseNotes ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        return new LauncherUpdateConfig(latestVersion, downloadUrl, releaseNotes);
    }
}
