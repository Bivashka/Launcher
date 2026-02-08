using BivLauncher.Api.Options;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Services;

public sealed class BuildSourcesLayoutService(
    IOptions<BuildPipelineOptions> options,
    IWebHostEnvironment environment,
    ILogger<BuildSourcesLayoutService> logger) : IBuildSourcesLayoutService
{
    private static readonly string[] ProfileTemplateDirectories =
    [
        "common",
        "common/mods",
        "common/config",
        "common/resourcepacks",
        "common/shaderpacks",
        "servers",
        "loaders",
        "loaders/vanilla/common",
        "loaders/forge/common",
        "loaders/fabric/common",
        "loaders/quilt/common",
        "loaders/neoforge/common",
        "loaders/liteloader/common"
    ];

    private static readonly string[] ServerTemplateDirectories =
    [
        "common",
        "common/mods",
        "common/config",
        "common/resourcepacks",
        "common/shaderpacks",
        "loaders",
        "loaders/vanilla/common",
        "loaders/forge/common",
        "loaders/fabric/common",
        "loaders/quilt/common",
        "loaders/neoforge/common",
        "loaders/liteloader/common"
    ];

    private readonly BuildPipelineOptions _options = options.Value;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ILogger<BuildSourcesLayoutService> _logger = logger;

    public void EnsureProfileLayout(string profileSlug, string? previousProfileSlug = null)
    {
        var sourceRoot = ResolveSourceRoot();
        Directory.CreateDirectory(sourceRoot);

        var normalizedSlug = NormalizeSlug(profileSlug);
        var profileRoot = ResolveChildPath(sourceRoot, normalizedSlug);

        if (!string.IsNullOrWhiteSpace(previousProfileSlug))
        {
            var previousNormalizedSlug = NormalizeSlug(previousProfileSlug);
            if (!string.Equals(previousNormalizedSlug, normalizedSlug, StringComparison.OrdinalIgnoreCase))
            {
                var previousRoot = ResolveChildPath(sourceRoot, previousNormalizedSlug);
                TryMoveDirectory(previousRoot, profileRoot);
            }
        }

        foreach (var relativePath in ProfileTemplateDirectories)
        {
            Directory.CreateDirectory(Path.Combine(profileRoot, relativePath));
        }

        EnsureFile(
            Path.Combine(profileRoot, "README.md"),
            BuildProfileReadme(normalizedSlug));
    }

    public void EnsureServerLayout(string profileSlug, Guid serverId, string serverName)
    {
        EnsureProfileLayout(profileSlug);

        var sourceRoot = ResolveSourceRoot();
        var profileRoot = ResolveChildPath(sourceRoot, NormalizeSlug(profileSlug));
        var roots = new[]
        {
            ResolveChildPath(profileRoot, $"servers/{serverId:N}"),
            ResolveChildPath(profileRoot, $"servers/{NormalizeServerName(serverName)}")
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var serverRoot in roots)
        {
            foreach (var relativePath in ServerTemplateDirectories)
            {
                Directory.CreateDirectory(Path.Combine(serverRoot, relativePath));
            }

            EnsureFile(
                Path.Combine(serverRoot, "README.md"),
                BuildServerReadme(serverId, serverName));
        }
    }

    private string ResolveSourceRoot()
    {
        var configuredRoot = _options.SourceRoot;
        return Path.IsPathRooted(configuredRoot)
            ? Path.GetFullPath(configuredRoot)
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configuredRoot));
    }

    private static string NormalizeSlug(string rawSlug)
    {
        var normalized = (rawSlug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Profile slug is empty.");
        }

        return normalized;
    }

    private static string ResolveChildPath(string parentPath, string relativePath)
    {
        var fullParentPath = Path.GetFullPath(parentPath);
        var candidatePath = Path.GetFullPath(Path.Combine(fullParentPath, relativePath));
        if (!candidatePath.StartsWith(fullParentPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escapes build source root.");
        }

        return candidatePath;
    }

    private void TryMoveDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath) || Directory.Exists(destinationPath))
        {
            return;
        }

        try
        {
            Directory.Move(sourcePath, destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not move build source directory from {SourcePath} to {DestinationPath}.",
                sourcePath,
                destinationPath);
        }
    }

    private static void EnsureFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, content);
    }

    private static string BuildProfileReadme(string profileSlug)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Profile build-sources scaffold for '{profileSlug}'.",
                string.Empty,
                "Default rebuild mode reads from this profile root:",
                "- common/",
                "- loaders/<loader>/common/",
                "- loaders/<loader>/<mcVersion>/",
                string.Empty,
                "Per-server folders are created under:",
                "- servers/<serverId>/",
                "- servers/<server-name>/",
                string.Empty,
                "If exactly one server folder has files, rebuild auto-merges it.",
                "If several server folders have files, set SourceSubPath manually."
            ]);
    }

    private static string BuildServerReadme(Guid serverId, string serverName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(serverName)
            ? "Server"
            : serverName.Trim();

        return string.Join(
            Environment.NewLine,
            [
                $"Server scaffold for '{normalizedName}' ({serverId}).",
                string.Empty,
                "Place optional per-server content here.",
                "Auto-rebuild can merge this folder when it is the only populated server folder.",
                "For exact control use SourceSubPath in profile rebuild."
            ]);
    }

    private static string NormalizeServerName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "server";
        }

        var chars = rawName
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "server" : normalized;
    }
}
