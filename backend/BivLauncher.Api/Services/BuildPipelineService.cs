using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class BuildPipelineService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IOptions<BuildPipelineOptions> options,
    IWebHostEnvironment environment,
    ILogger<BuildPipelineService> logger) : IBuildPipelineService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<BuildDto> RebuildProfileAsync(Guid profileId, ProfileRebuildRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.Profiles
            .Include(x => x.Servers)
            .FirstOrDefaultAsync(x => x.Id == profileId, cancellationToken);

        if (profile is null)
        {
            throw new KeyNotFoundException("Profile not found.");
        }

        var loaderType = LoaderCatalog.NormalizeLoader(ResolveValue(request.LoaderType, profile.Servers.FirstOrDefault()?.LoaderType, "vanilla"));
        if (!LoaderCatalog.IsSupported(loaderType))
        {
            throw new InvalidOperationException($"Unsupported loader type '{loaderType}'. Allowed: {string.Join(", ", LoaderCatalog.SupportedLoaders)}.");
        }

        var mcVersion = ResolveValue(request.McVersion, profile.Servers.FirstOrDefault()?.McVersion, "1.21.1");
        var sourceDirectories = ResolveSourceDirectories(profile.Slug, request.SourceSubPath, loaderType, mcVersion);
        if (sourceDirectories.Count == 0)
        {
            throw new DirectoryNotFoundException($"Source directory for profile '{profile.Slug}' does not exist.");
        }

        var clientVersion = string.IsNullOrWhiteSpace(request.ClientVersion)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmm")
            : request.ClientVersion.Trim();
        var javaRuntimePath = ResolveJavaRuntimePath(request.JavaRuntimePath, profile.BundledJavaPath);
        var javaRuntimeArtifactKey = ResolveRuntimeArtifactKey(request.JavaRuntimeArtifactKey, profile.BundledRuntimeKey);
        var javaRuntimeArtifactMetadata = await ResolveRuntimeArtifactMetadataAsync(javaRuntimeArtifactKey, profile, cancellationToken);
        var launchProfile = ResolveLaunchProfile(request, loaderType);

        var buildId = Guid.NewGuid();
        var build = new Build
        {
            Id = buildId,
            ProfileId = profile.Id,
            LoaderType = loaderType,
            McVersion = mcVersion,
            ClientVersion = clientVersion,
            Status = BuildStatus.Building
        };

        dbContext.Builds.Add(build);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var sourceFiles = CollectSourceFiles(sourceDirectories);
            if (sourceFiles.Count == 0)
            {
                throw new DirectoryNotFoundException("No files found in build source directories.");
            }

            var manifestFiles = new List<LauncherManifestFile>(sourceFiles.Count);
            long totalSize = 0;
            var buildIdString = buildId.ToString("N");

            foreach (var sourceFile in sourceFiles.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = sourceFile.Key;
                var filePath = sourceFile.Value;
                var s3Key = $"clients/{profile.Slug}/{buildIdString}/{relativePath}";
                var contentType = ResolveContentType(filePath);

                await using var stream = File.OpenRead(filePath);
                var sha256 = await ComputeSha256Async(stream, cancellationToken);
                stream.Position = 0;

                await objectStorageService.UploadAsync(s3Key, stream, contentType, cancellationToken: cancellationToken);

                var fileSize = stream.Length;
                totalSize += fileSize;

                manifestFiles.Add(new LauncherManifestFile(
                    Path: relativePath,
                    Sha256: sha256,
                    Size: fileSize,
                    S3Key: s3Key));
            }

            var manifest = new LauncherManifest(
                ProfileSlug: profile.Slug,
                BuildId: buildIdString,
                LoaderType: loaderType,
                McVersion: mcVersion,
                ClientVersion: clientVersion,
                CreatedAtUtc: DateTime.UtcNow,
                JvmArgsDefault: ResolveValue(request.JvmArgsDefault, options.Value.DefaultJvmArgs, "-Xms1024M -Xmx2048M"),
                GameArgsDefault: ResolveValue(request.GameArgsDefault, options.Value.DefaultGameArgs, string.Empty),
                JavaRuntime: javaRuntimePath,
                JavaRuntimeArtifactKey: javaRuntimeArtifactKey,
                JavaRuntimeArtifactSha256: javaRuntimeArtifactMetadata.Sha256,
                JavaRuntimeArtifactSizeBytes: javaRuntimeArtifactMetadata.SizeBytes,
                JavaRuntimeArtifactContentType: javaRuntimeArtifactMetadata.ContentType,
                Files: manifestFiles,
                LaunchMode: launchProfile.Mode,
                LaunchMainClass: launchProfile.MainClass,
                LaunchClasspath: launchProfile.ClasspathEntries);

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
            var buildManifestKey = $"manifests/{profile.Slug}/{buildIdString}.json";
            var latestManifestKey = $"manifests/{profile.Slug}/latest.json";

            await using (var buildManifestStream = new MemoryStream(manifestBytes, writable: false))
            {
                await objectStorageService.UploadAsync(buildManifestKey, buildManifestStream, "application/json", cancellationToken: cancellationToken);
            }

            await using (var latestManifestStream = new MemoryStream(manifestBytes, writable: false))
            {
                await objectStorageService.UploadAsync(latestManifestKey, latestManifestStream, "application/json", cancellationToken: cancellationToken);
            }

            if (request.PublishToServers)
            {
                foreach (var server in profile.Servers)
                {
                    server.BuildId = buildIdString;
                }
            }

            profile.LatestBuildId = buildIdString;
            profile.LatestManifestKey = latestManifestKey;
            profile.LatestClientVersion = clientVersion;

            build.Status = BuildStatus.Completed;
            build.ManifestKey = buildManifestKey;
            build.FilesCount = manifestFiles.Count;
            build.TotalSizeBytes = totalSize;
            build.ErrorMessage = string.Empty;

            await dbContext.SaveChangesAsync(cancellationToken);
            return MapBuild(build);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profile rebuild failed for profile {ProfileId}", profileId);
            build.Status = BuildStatus.Failed;
            build.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private IReadOnlyList<string> ResolveSourceDirectories(string profileSlug, string sourceSubPath, string loaderType, string mcVersion)
    {
        var sourceRoot = Path.IsPathRooted(options.Value.SourceRoot)
            ? options.Value.SourceRoot
            : Path.Combine(environment.ContentRootPath, options.Value.SourceRoot);

        var profileRoot = Path.GetFullPath(Path.Combine(sourceRoot, profileSlug));
        if (!Directory.Exists(profileRoot))
        {
            throw new DirectoryNotFoundException($"Source directory '{profileRoot}' does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(sourceSubPath))
        {
            var targetPath = Path.GetFullPath(Path.Combine(profileRoot, sourceSubPath.Trim()));
            if (!targetPath.StartsWith(profileRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SourceSubPath escapes profile source directory.");
            }

            if (!Directory.Exists(targetPath))
            {
                throw new DirectoryNotFoundException($"Source directory '{targetPath}' does not exist.");
            }

            return [targetPath];
        }

        var selected = new List<string>();
        var commonDirectory = Path.Combine(profileRoot, "common");
        if (Directory.Exists(commonDirectory))
        {
            selected.Add(commonDirectory);
        }

        var loaderCommonDirectory = Path.Combine(profileRoot, "loaders", loaderType, "common");
        if (Directory.Exists(loaderCommonDirectory))
        {
            selected.Add(loaderCommonDirectory);
        }

        var loaderVersionDirectory = Path.Combine(profileRoot, "loaders", loaderType, mcVersion);
        if (Directory.Exists(loaderVersionDirectory))
        {
            selected.Add(loaderVersionDirectory);
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        return [profileRoot];
    }

    private static string ResolveValue(string? preferred, string? fallback, string hardFallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return hardFallback;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static LaunchProfile ResolveLaunchProfile(ProfileRebuildRequest request, string loaderType)
    {
        var requestedMode = (request.LaunchMode ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedMainClass = (request.LaunchMainClass ?? string.Empty).Trim();
        var classpathEntries = ParseClasspathEntries(request.LaunchClasspath);

        if (requestedMode is "" or "auto")
        {
            if (IsMainClassLoader(loaderType))
            {
                var mainClass = string.IsNullOrWhiteSpace(normalizedMainClass)
                    ? GetDefaultMainClass(loaderType)
                    : normalizedMainClass;
                if (string.IsNullOrWhiteSpace(mainClass))
                {
                    throw new InvalidOperationException($"LaunchMainClass is required for loader '{loaderType}'.");
                }

                if (classpathEntries.Count == 0)
                {
                    classpathEntries.Add("libraries/**/*.jar");
                    classpathEntries.Add("*.jar");
                }

                return new LaunchProfile("mainclass", mainClass, classpathEntries);
            }

            return new LaunchProfile("jar", string.Empty, []);
        }

        if (requestedMode == "jar")
        {
            return new LaunchProfile("jar", string.Empty, []);
        }

        if (requestedMode is "mainclass" or "main-class")
        {
            if (string.IsNullOrWhiteSpace(normalizedMainClass))
            {
                throw new InvalidOperationException("LaunchMainClass is required when LaunchMode is 'mainclass'.");
            }

            if (classpathEntries.Count == 0)
            {
                throw new InvalidOperationException("LaunchClasspath is required when LaunchMode is 'mainclass'.");
            }

            return new LaunchProfile("mainclass", normalizedMainClass, classpathEntries);
        }

        throw new InvalidOperationException("LaunchMode must be one of: auto, jar, mainclass.");
    }

    private static List<string> ParseClasspathEntries(string? rawClasspath)
    {
        if (string.IsNullOrWhiteSpace(rawClasspath))
        {
            return [];
        }

        var entries = rawClasspath
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeClasspathEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return entries;
    }

    private static string NormalizeClasspathEntry(string entry)
    {
        var normalized = entry.Replace('\\', '/').Trim().TrimStart('/');
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException($"LaunchClasspath entry '{entry}' must be relative.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x == ".."))
        {
            throw new InvalidOperationException($"LaunchClasspath entry '{entry}' cannot contain '..'.");
        }

        return string.Join('/', segments);
    }

    private static bool IsMainClassLoader(string loaderType)
    {
        return loaderType is "forge" or "neoforge" or "fabric" or "quilt";
    }

    private static string GetDefaultMainClass(string loaderType)
    {
        return loaderType switch
        {
            "forge" => "cpw.mods.modlauncher.Launcher",
            "neoforge" => "cpw.mods.modlauncher.Launcher",
            "fabric" => "net.fabricmc.loader.impl.launch.knot.KnotClient",
            "quilt" => "org.quiltmc.loader.impl.launch.knot.KnotClient",
            _ => string.Empty
        };
    }

    private sealed record LaunchProfile(
        string Mode,
        string MainClass,
        IReadOnlyList<string> ClasspathEntries);

    private static Dictionary<string, string> CollectSourceFiles(IReadOnlyList<string> sourceDirectories)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceDirectory in sourceDirectories)
        {
            foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = NormalizePath(Path.GetRelativePath(sourceDirectory, filePath));
                files[relativePath] = filePath;
            }
        }

        return files;
    }

    private static string? ResolveJavaRuntimePath(string? preferred, string? fallback)
    {
        var selected = !string.IsNullOrWhiteSpace(preferred)
            ? preferred.Trim()
            : fallback?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(selected))
        {
            return null;
        }

        var normalized = selected.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("JavaRuntimePath must be relative to the instance directory.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x == ".."))
        {
            throw new InvalidOperationException("JavaRuntimePath cannot contain path traversal segments.");
        }

        return string.Join('/', segments);
    }

    private static string? ResolveRuntimeArtifactKey(string? preferred, string? fallback)
    {
        var selected = !string.IsNullOrWhiteSpace(preferred)
            ? preferred.Trim()
            : fallback?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(selected))
        {
            return null;
        }

        var normalized = selected.Replace('\\', '/').TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task<RuntimeArtifactMetadata> ResolveRuntimeArtifactMetadataAsync(
        string? runtimeArtifactKey,
        Profile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeArtifactKey))
        {
            return new RuntimeArtifactMetadata(null, null, null);
        }

        var profileRuntimeKey = ResolveRuntimeArtifactKey(profile.BundledRuntimeKey, null);
        var fromProfile = string.Equals(runtimeArtifactKey, profileRuntimeKey, StringComparison.OrdinalIgnoreCase)
            ? CreateRuntimeArtifactMetadataFromProfile(profile)
            : new RuntimeArtifactMetadata(null, null, null);

        if (!string.IsNullOrWhiteSpace(fromProfile.Sha256) &&
            fromProfile.SizeBytes is > 0 &&
            !string.IsNullOrWhiteSpace(fromProfile.ContentType))
        {
            return fromProfile;
        }

        var fromStorage = await objectStorageService.GetMetadataAsync(runtimeArtifactKey, cancellationToken);
        if (fromStorage is null)
        {
            return fromProfile;
        }

        var sha256 = !string.IsNullOrWhiteSpace(fromProfile.Sha256)
            ? fromProfile.Sha256
            : string.IsNullOrWhiteSpace(fromStorage.Sha256) ? null : fromStorage.Sha256.Trim().ToLowerInvariant();
        long? sizeBytes = fromProfile.SizeBytes is > 0
            ? fromProfile.SizeBytes
            : fromStorage.SizeBytes > 0 ? fromStorage.SizeBytes : null;
        var contentType = !string.IsNullOrWhiteSpace(fromProfile.ContentType)
            ? fromProfile.ContentType
            : string.IsNullOrWhiteSpace(fromStorage.ContentType) ? null : fromStorage.ContentType.Trim();

        return new RuntimeArtifactMetadata(sha256, sizeBytes, contentType);
    }

    private static RuntimeArtifactMetadata CreateRuntimeArtifactMetadataFromProfile(Profile profile)
    {
        var sha256 = string.IsNullOrWhiteSpace(profile.BundledRuntimeSha256)
            ? null
            : profile.BundledRuntimeSha256.Trim().ToLowerInvariant();
        long? sizeBytes = profile.BundledRuntimeSizeBytes > 0 ? profile.BundledRuntimeSizeBytes : null;
        var contentType = string.IsNullOrWhiteSpace(profile.BundledRuntimeContentType)
            ? null
            : profile.BundledRuntimeContentType.Trim();
        return new RuntimeArtifactMetadata(sha256, sizeBytes, contentType);
    }

    private sealed record RuntimeArtifactMetadata(
        string? Sha256,
        long? SizeBytes,
        string? ContentType);

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".json" => "application/json",
            ".txt" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".jar" => "application/java-archive",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    private static BuildDto MapBuild(Build build)
    {
        return new BuildDto(
            build.Id,
            build.ProfileId,
            build.LoaderType,
            build.McVersion,
            build.CreatedAtUtc,
            build.Status,
            build.ManifestKey,
            build.ClientVersion,
            build.ErrorMessage,
            build.FilesCount,
            build.TotalSizeBytes);
    }
}
