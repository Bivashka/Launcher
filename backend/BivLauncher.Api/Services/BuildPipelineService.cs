using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class BuildPipelineService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IOptions<BuildPipelineOptions> options,
    IWebHostEnvironment environment,
    ILogger<BuildPipelineService> logger) : IBuildPipelineService
{
    private const string LegacyBridgeInternalName = "com/mojang/authlib/yggdrasil/LegacyBridge";
    private const string LegacyBridgeClassEntry = LegacyBridgeInternalName + ".class";
    private const string LegacyBridgeCompatArtifactPath = "libraries/00-legacybridge-compat.jar";

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
        var sourceDirectories = ResolveSourceDirectories(profile.Slug, request.SourceSubPath, loaderType, mcVersion, profile.Servers);
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

            var generatedFiles = BuildGeneratedFiles(sourceFiles);
            launchProfile = EnsureGeneratedFilesAreOnClasspath(launchProfile, generatedFiles);

            var manifestFiles = new List<LauncherManifestFile>(sourceFiles.Count + generatedFiles.Count);
            var skippedMissingFiles = new List<string>();
            long totalSize = 0;
            var buildIdString = buildId.ToString("N");

            foreach (var sourceFile in sourceFiles.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = sourceFile.Key;
                var filePath = sourceFile.Value;
                var s3Key = $"clients/{profile.Slug}/{buildIdString}/{relativePath}";
                var contentType = ResolveContentType(filePath);

                try
                {
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
                catch (FileNotFoundException)
                {
                    skippedMissingFiles.Add(relativePath);
                    logger.LogWarning(
                        "Skipping source file '{RelativePath}' because it no longer exists at build time: {FilePath}",
                        relativePath,
                        filePath);
                }
                catch (DirectoryNotFoundException)
                {
                    skippedMissingFiles.Add(relativePath);
                    logger.LogWarning(
                        "Skipping source file '{RelativePath}' because its directory no longer exists at build time: {FilePath}",
                        relativePath,
                        filePath);
                }
            }

            if (manifestFiles.Count == 0)
            {
                if (skippedMissingFiles.Count > 0)
                {
                    throw new DirectoryNotFoundException(
                        $"No readable files found in build source directories. " +
                        $"Skipped {skippedMissingFiles.Count} missing file(s); first missing path: '{skippedMissingFiles[0]}'.");
                }

                throw new DirectoryNotFoundException("No files found in build source directories.");
            }

            if (skippedMissingFiles.Count > 0)
            {
                logger.LogWarning(
                    "Skipped {MissingFilesCount} source file(s) that disappeared during rebuild for profile '{ProfileSlug}'.",
                    skippedMissingFiles.Count,
                    profile.Slug);
            }

            foreach (var generatedFile in generatedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var s3Key = $"clients/{profile.Slug}/{buildIdString}/{generatedFile.Path}";
                var sha256 = Convert.ToHexString(SHA256.HashData(generatedFile.Content)).ToLowerInvariant();
                await using var stream = new MemoryStream(generatedFile.Content, writable: false);

                await objectStorageService.UploadAsync(s3Key, stream, generatedFile.ContentType, cancellationToken: cancellationToken);

                var fileSize = stream.Length;
                totalSize += fileSize;

                manifestFiles.Add(new LauncherManifestFile(
                    Path: generatedFile.Path,
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
                JvmArgsDefault: ResolveValue(request.JvmArgsDefault, profile.JvmArgsDefault, options.Value.DefaultJvmArgs, "-Xms1024M -Xmx2048M"),
                GameArgsDefault: ResolveValue(request.GameArgsDefault, profile.GameArgsDefault, options.Value.DefaultGameArgs, string.Empty),
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

    private IReadOnlyList<string> ResolveSourceDirectories(
        string profileSlug,
        string sourceSubPath,
        string loaderType,
        string mcVersion,
        ICollection<Server> servers)
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

        var selected = ResolveProfileLayerDirectories(profileRoot, loaderType, mcVersion).ToList();
        var autoResolvedServerLayers = TryResolveAutomaticServerLayerDirectories(
            profileRoot,
            loaderType,
            mcVersion,
            servers,
            out var serverLayers,
            out var hasMultiplePopulatedServerSources);
        if (autoResolvedServerLayers)
        {
            selected.AddRange(serverLayers);
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        if (hasMultiplePopulatedServerSources)
        {
            throw new InvalidOperationException(
                $"Multiple populated server source directories detected for profile '{profileSlug}'. " +
                "Specify SourceSubPath to select one server folder.");
        }

        return [profileRoot];
    }

    private static IReadOnlyList<string> ResolveProfileLayerDirectories(string profileRoot, string loaderType, string mcVersion)
    {
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

        return selected;
    }

    private bool TryResolveAutomaticServerLayerDirectories(
        string profileRoot,
        string loaderType,
        string mcVersion,
        ICollection<Server> servers,
        out IReadOnlyList<string> resolvedServerLayers,
        out bool hasMultiplePopulatedServerSources)
    {
        var serverLayerGroups = servers
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(server => new
            {
                ServerId = server.Id,
                ServerName = server.Name,
                Layers = ResolveServerLayerDirectories(profileRoot, loaderType, mcVersion, server).ToList()
            })
            .Where(x => x.Layers.Count > 0 && x.Layers.Any(DirectoryContainsFiles))
            .ToList();

        if (serverLayerGroups.Count == 0)
        {
            resolvedServerLayers = [];
            hasMultiplePopulatedServerSources = false;
            return false;
        }

        if (serverLayerGroups.Count > 1)
        {
            logger.LogInformation(
                "Multiple server-specific source directories detected for profile root {ProfileRoot}. " +
                "Using profile-level directories only. Set SourceSubPath manually to target one server.",
                profileRoot);
            resolvedServerLayers = [];
            hasMultiplePopulatedServerSources = true;
            return false;
        }

        var selectedServer = serverLayerGroups[0];
        logger.LogInformation(
            "Auto-selected server-specific source directories for server {ServerId} ({ServerName}).",
            selectedServer.ServerId,
            selectedServer.ServerName);
        resolvedServerLayers = selectedServer.Layers;
        hasMultiplePopulatedServerSources = false;
        return true;
    }

    private static IReadOnlyList<string> ResolveServerLayerDirectories(
        string profileRoot,
        string loaderType,
        string mcVersion,
        Server server)
    {
        var byIdRoot = Path.Combine(profileRoot, "servers", server.Id.ToString("N"));
        var byNameRoot = Path.Combine(profileRoot, "servers", NormalizeServerName(server.Name));

        var candidates = new List<string>();
        foreach (var root in new[] { byIdRoot, byNameRoot })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var commonDirectory = Path.Combine(root, "common");
            if (Directory.Exists(commonDirectory))
            {
                candidates.Add(commonDirectory);
            }

            var loaderCommonDirectory = Path.Combine(root, "loaders", loaderType, "common");
            if (Directory.Exists(loaderCommonDirectory))
            {
                candidates.Add(loaderCommonDirectory);
            }

            var loaderVersionDirectory = Path.Combine(root, "loaders", loaderType, mcVersion);
            if (Directory.Exists(loaderVersionDirectory))
            {
                candidates.Add(loaderVersionDirectory);
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool DirectoryContainsFiles(string path)
    {
        return Directory.Exists(path) &&
               Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
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

    private static string ResolveValue(string? preferred, string? fallback, string nextFallback, string hardFallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        if (!string.IsNullOrWhiteSpace(nextFallback))
        {
            return nextFallback.Trim();
        }

        return hardFallback;
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

        return rawClasspath
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeClasspathEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static LaunchProfile EnsureGeneratedFilesAreOnClasspath(
        LaunchProfile launchProfile,
        IReadOnlyList<GeneratedManifestFile> generatedFiles)
    {
        if (!string.Equals(launchProfile.Mode, "mainclass", StringComparison.OrdinalIgnoreCase) ||
            generatedFiles.Count == 0)
        {
            return launchProfile;
        }

        var classpathEntries = launchProfile.ClasspathEntries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var generatedFile in generatedFiles)
        {
            if (!classpathEntries.Contains(generatedFile.Path, StringComparer.OrdinalIgnoreCase))
            {
                classpathEntries.Add(generatedFile.Path);
            }
        }

        return new LaunchProfile(launchProfile.Mode, launchProfile.MainClass, classpathEntries);
    }

    private List<GeneratedManifestFile> BuildGeneratedFiles(IReadOnlyDictionary<string, string> sourceFiles)
    {
        var generatedFiles = new List<GeneratedManifestFile>();

        var legacyBridgeCompatFile = TryBuildLegacyBridgeCompatFile(sourceFiles);
        if (legacyBridgeCompatFile is not null)
        {
            generatedFiles.Add(legacyBridgeCompatFile);
        }

        return generatedFiles;
    }

    private GeneratedManifestFile? TryBuildLegacyBridgeCompatFile(IReadOnlyDictionary<string, string> sourceFiles)
    {
        var existingRelativePaths = new HashSet<string>(sourceFiles.Keys, StringComparer.OrdinalIgnoreCase);
        var archivePaths = sourceFiles.Values
            .Where(IsArchiveCandidate)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (archivePaths.Count == 0)
        {
            return null;
        }

        var requiredMethods = new HashSet<LegacyBridgeMethodSignature>();
        foreach (var archivePath in archivePaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                if (archive.Entries.Any(x => x.FullName.Equals(LegacyBridgeClassEntry, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                foreach (var classEntry in archive.Entries.Where(x => x.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase)))
                {
                    using var entryStream = classEntry.Open();
                    using var memory = new MemoryStream();
                    entryStream.CopyTo(memory);
                    TryCollectLegacyBridgeMethodRefs(memory.ToArray(), requiredMethods);
                }
            }
            catch (Exception ex) when (
                ex is InvalidDataException or
                IOException or
                UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Failed to inspect archive '{ArchivePath}' while checking LegacyBridge compatibility.", archivePath);
            }
        }

        if (requiredMethods.Count == 0)
        {
            return null;
        }

        var artifactPath = ResolveGeneratedArtifactPath(existingRelativePaths, LegacyBridgeCompatArtifactPath);
        var compatJarBytes = BuildLegacyBridgeCompatJar(requiredMethods);
        logger.LogWarning(
            "Generated LegacyBridge compatibility artifact '{ArtifactPath}' with {MethodCount} method signature(s).",
            artifactPath,
            requiredMethods.Count);

        return new GeneratedManifestFile(
            Path: artifactPath,
            Content: compatJarBytes,
            ContentType: "application/java-archive");
    }

    private static bool IsArchiveCandidate(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jar2", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveGeneratedArtifactPath(IReadOnlySet<string> existingRelativePaths, string preferredPath)
    {
        if (!existingRelativePaths.Contains(preferredPath))
        {
            return preferredPath;
        }

        var directory = NormalizePath(Path.GetDirectoryName(preferredPath) ?? string.Empty).Trim('/');
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(preferredPath);
        var extension = Path.GetExtension(preferredPath);

        for (var suffix = 1; suffix <= 1024; suffix++)
        {
            var candidateFileName = $"{fileNameWithoutExtension}-{suffix}{extension}";
            var candidatePath = string.IsNullOrWhiteSpace(directory)
                ? candidateFileName
                : $"{directory}/{candidateFileName}";
            if (!existingRelativePaths.Contains(candidatePath))
            {
                return candidatePath;
            }
        }

        return preferredPath;
    }

    private static void TryCollectLegacyBridgeMethodRefs(
        byte[] classFileBytes,
        ISet<LegacyBridgeMethodSignature> requiredMethods)
    {
        var span = classFileBytes.AsSpan();
        var offset = 0;

        if (!TryReadU4(span, ref offset, out var magic) || magic != 0xCAFEBABE)
        {
            return;
        }

        if (!TrySkip(span, ref offset, 4))
        {
            return;
        }

        if (!TryReadU2(span, ref offset, out var constantPoolCount) || constantPoolCount == 0)
        {
            return;
        }

        var tags = new byte[constantPoolCount];
        var utf8Values = new string?[constantPoolCount];
        var classNameIndices = new ushort[constantPoolCount];
        var nameIndices = new ushort[constantPoolCount];
        var descriptorIndices = new ushort[constantPoolCount];
        var methodClassIndices = new ushort[constantPoolCount];
        var methodNameAndTypeIndices = new ushort[constantPoolCount];

        for (var index = 1; index < constantPoolCount; index++)
        {
            if (!TryReadU1(span, ref offset, out var tag))
            {
                return;
            }

            tags[index] = tag;

            switch (tag)
            {
                case 1:
                    if (!TryReadU2(span, ref offset, out var utfLength) ||
                        !TryReadBytes(span, ref offset, utfLength, out var utfBytes))
                    {
                        return;
                    }

                    utf8Values[index] = Encoding.UTF8.GetString(utfBytes);
                    break;
                case 3:
                case 4:
                    if (!TrySkip(span, ref offset, 4))
                    {
                        return;
                    }

                    break;
                case 5:
                case 6:
                    if (!TrySkip(span, ref offset, 8))
                    {
                        return;
                    }

                    index++;
                    break;
                case 7:
                    if (!TryReadU2(span, ref offset, out classNameIndices[index]))
                    {
                        return;
                    }

                    break;
                case 8:
                case 16:
                case 19:
                case 20:
                    if (!TrySkip(span, ref offset, 2))
                    {
                        return;
                    }

                    break;
                case 9:
                case 10:
                case 11:
                case 12:
                case 17:
                case 18:
                    if (!TryReadU2(span, ref offset, out var firstIndex) ||
                        !TryReadU2(span, ref offset, out var secondIndex))
                    {
                        return;
                    }

                    if (tag == 12)
                    {
                        nameIndices[index] = firstIndex;
                        descriptorIndices[index] = secondIndex;
                    }
                    else if (tag == 10 || tag == 11)
                    {
                        methodClassIndices[index] = firstIndex;
                        methodNameAndTypeIndices[index] = secondIndex;
                    }

                    break;
                case 15:
                    if (!TrySkip(span, ref offset, 3))
                    {
                        return;
                    }

                    break;
                default:
                    return;
            }
        }

        for (var index = 1; index < constantPoolCount; index++)
        {
            if (tags[index] is not 10 and not 11)
            {
                continue;
            }

            var classIndex = methodClassIndices[index];
            var nameAndTypeIndex = methodNameAndTypeIndices[index];
            if (classIndex <= 0 ||
                nameAndTypeIndex <= 0 ||
                classIndex >= constantPoolCount ||
                nameAndTypeIndex >= constantPoolCount ||
                tags[classIndex] != 7 ||
                tags[nameAndTypeIndex] != 12)
            {
                continue;
            }

            var classNameIndex = classNameIndices[classIndex];
            if (classNameIndex <= 0 || classNameIndex >= constantPoolCount)
            {
                continue;
            }

            var className = utf8Values[classNameIndex];
            if (!string.Equals(className, LegacyBridgeInternalName, StringComparison.Ordinal))
            {
                continue;
            }

            var methodNameIndex = nameIndices[nameAndTypeIndex];
            var methodDescriptorIndex = descriptorIndices[nameAndTypeIndex];
            if (methodNameIndex <= 0 ||
                methodDescriptorIndex <= 0 ||
                methodNameIndex >= constantPoolCount ||
                methodDescriptorIndex >= constantPoolCount)
            {
                continue;
            }

            var methodName = utf8Values[methodNameIndex];
            var descriptor = utf8Values[methodDescriptorIndex];
            if (string.IsNullOrWhiteSpace(methodName) ||
                string.IsNullOrWhiteSpace(descriptor) ||
                methodName[0] == '<' ||
                descriptor[0] != '(' ||
                !descriptor.Contains(')'))
            {
                continue;
            }

            requiredMethods.Add(new LegacyBridgeMethodSignature(methodName, descriptor));
        }
    }

    private static bool TryReadU1(ReadOnlySpan<byte> span, ref int offset, out byte value)
    {
        if (offset + 1 > span.Length)
        {
            value = 0;
            return false;
        }

        value = span[offset];
        offset++;
        return true;
    }

    private static bool TryReadU2(ReadOnlySpan<byte> span, ref int offset, out ushort value)
    {
        if (offset + 2 > span.Length)
        {
            value = 0;
            return false;
        }

        value = (ushort)((span[offset] << 8) | span[offset + 1]);
        offset += 2;
        return true;
    }

    private static bool TryReadU4(ReadOnlySpan<byte> span, ref int offset, out uint value)
    {
        if (offset + 4 > span.Length)
        {
            value = 0;
            return false;
        }

        value = ((uint)span[offset] << 24) |
                ((uint)span[offset + 1] << 16) |
                ((uint)span[offset + 2] << 8) |
                span[offset + 3];
        offset += 4;
        return true;
    }

    private static bool TryReadBytes(ReadOnlySpan<byte> span, ref int offset, int length, out ReadOnlySpan<byte> slice)
    {
        if (length < 0 || offset + length > span.Length)
        {
            slice = ReadOnlySpan<byte>.Empty;
            return false;
        }

        slice = span.Slice(offset, length);
        offset += length;
        return true;
    }

    private static bool TrySkip(ReadOnlySpan<byte> span, ref int offset, int length)
    {
        if (length < 0 || offset + length > span.Length)
        {
            return false;
        }

        offset += length;
        return true;
    }

    private static byte[] BuildLegacyBridgeCompatJar(IReadOnlyCollection<LegacyBridgeMethodSignature> requiredMethods)
    {
        var classBytes = BuildLegacyBridgeClass(requiredMethods);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("META-INF/MANIFEST.MF", CompressionLevel.Fastest);
            using (var writer = new StreamWriter(
                       manifestEntry.Open(),
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                       bufferSize: 1024,
                       leaveOpen: false))
            {
                writer.Write("Manifest-Version: 1.0\r\nCreated-By: BivLauncher\r\n\r\n");
            }

            var classEntry = archive.CreateEntry(LegacyBridgeClassEntry, CompressionLevel.Optimal);
            using var classStream = classEntry.Open();
            classStream.Write(classBytes, 0, classBytes.Length);
        }

        return stream.ToArray();
    }

    private static byte[] BuildLegacyBridgeClass(IReadOnlyCollection<LegacyBridgeMethodSignature> requiredMethods)
    {
        var pool = new ConstantPoolBuilder();

        var thisClassIndex = pool.AddClass(LegacyBridgeInternalName);
        var superClassIndex = pool.AddClass("java/lang/Object");
        var codeAttributeNameIndex = pool.AddUtf8("Code");

        var ctorNameIndex = pool.AddUtf8("<init>");
        var ctorDescriptorIndex = pool.AddUtf8("()V");
        var objectCtorMethodRefIndex = pool.AddMethodRef("java/lang/Object", "<init>", "()V");

        var methodIndices = requiredMethods
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Descriptor, StringComparer.Ordinal)
            .Select(x => new MethodIndex(
                NameIndex: pool.AddUtf8(x.Name),
                DescriptorIndex: pool.AddUtf8(x.Descriptor),
                Descriptor: x.Descriptor))
            .ToList();

        using var stream = new MemoryStream();
        WriteU4(stream, 0xCAFEBABE);
        WriteU2(stream, 0);
        WriteU2(stream, 52);
        WriteU2(stream, pool.Count);
        pool.WriteTo(stream);

        WriteU2(stream, 0x0021);
        WriteU2(stream, thisClassIndex);
        WriteU2(stream, superClassIndex);
        WriteU2(stream, 0);
        WriteU2(stream, 0);
        WriteU2(stream, checked((ushort)(1 + methodIndices.Count)));

        var ctorCode = BuildConstructorCode(objectCtorMethodRefIndex);
        WriteMethod(
            stream,
            accessFlags: 0x0001,
            nameIndex: ctorNameIndex,
            descriptorIndex: ctorDescriptorIndex,
            codeAttributeNameIndex: codeAttributeNameIndex,
            maxStack: 1,
            maxLocals: 1,
            code: ctorCode);

        foreach (var method in methodIndices)
        {
            var (stubCode, maxStack) = BuildStubCodeForDescriptor(method.Descriptor);
            var maxLocals = checked((ushort)GetMethodParameterSlotCount(method.Descriptor));
            WriteMethod(
                stream,
                accessFlags: 0x0009,
                nameIndex: method.NameIndex,
                descriptorIndex: method.DescriptorIndex,
                codeAttributeNameIndex: codeAttributeNameIndex,
                maxStack: maxStack,
                maxLocals: maxLocals,
                code: stubCode);
        }

        WriteU2(stream, 0);
        return stream.ToArray();
    }

    private static byte[] BuildConstructorCode(ushort objectCtorMethodRefIndex)
    {
        return
        [
            0x2A,
            0xB7,
            (byte)(objectCtorMethodRefIndex >> 8),
            (byte)(objectCtorMethodRefIndex & 0xFF),
            0xB1
        ];
    }

    private static (byte[] Code, ushort MaxStack) BuildStubCodeForDescriptor(string descriptor)
    {
        var returnType = GetMethodReturnType(descriptor);
        return returnType switch
        {
            'V' => ([0xB1], 0),
            'J' => ([0x09, 0xAD], 2),
            'D' => ([0x0E, 0xAF], 2),
            'F' => ([0x0B, 0xAE], 1),
            'I' or 'Z' or 'B' or 'C' or 'S' => ([0x03, 0xAC], 1),
            _ => ([0x01, 0xB0], 1)
        };
    }

    private static char GetMethodReturnType(string descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return 'L';
        }

        var closeParen = descriptor.IndexOf(')');
        if (closeParen < 0 || closeParen + 1 >= descriptor.Length)
        {
            return 'L';
        }

        return descriptor[closeParen + 1];
    }

    private static int GetMethodParameterSlotCount(string descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return 0;
        }

        var openParen = descriptor.IndexOf('(');
        var closeParen = descriptor.IndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return 0;
        }

        var slots = 0;
        for (var index = openParen + 1; index < closeParen;)
        {
            var type = descriptor[index];
            switch (type)
            {
                case 'B':
                case 'C':
                case 'F':
                case 'I':
                case 'S':
                case 'Z':
                    slots++;
                    index++;
                    break;
                case 'J':
                case 'D':
                    slots += 2;
                    index++;
                    break;
                case 'L':
                {
                    var semicolonIndex = descriptor.IndexOf(';', index);
                    if (semicolonIndex < 0 || semicolonIndex > closeParen)
                    {
                        return slots;
                    }

                    slots++;
                    index = semicolonIndex + 1;
                    break;
                }
                case '[':
                {
                    while (index < closeParen && descriptor[index] == '[')
                    {
                        index++;
                    }

                    if (index >= closeParen)
                    {
                        return slots;
                    }

                    if (descriptor[index] == 'L')
                    {
                        var semicolonIndex = descriptor.IndexOf(';', index);
                        if (semicolonIndex < 0 || semicolonIndex > closeParen)
                        {
                            return slots;
                        }

                        index = semicolonIndex + 1;
                    }
                    else
                    {
                        index++;
                    }

                    slots++;
                    break;
                }
                default:
                    index++;
                    break;
            }
        }

        return slots;
    }

    private static void WriteMethod(
        Stream stream,
        ushort accessFlags,
        ushort nameIndex,
        ushort descriptorIndex,
        ushort codeAttributeNameIndex,
        ushort maxStack,
        ushort maxLocals,
        byte[] code)
    {
        WriteU2(stream, accessFlags);
        WriteU2(stream, nameIndex);
        WriteU2(stream, descriptorIndex);
        WriteU2(stream, 1);

        WriteU2(stream, codeAttributeNameIndex);
        var attributeLength = checked((uint)(2 + 2 + 4 + code.Length + 2 + 2));
        WriteU4(stream, attributeLength);

        WriteU2(stream, maxStack);
        WriteU2(stream, maxLocals);
        WriteU4(stream, checked((uint)code.Length));
        stream.Write(code, 0, code.Length);
        WriteU2(stream, 0);
        WriteU2(stream, 0);
    }

    private static void WriteU2(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteU4(Stream stream, uint value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private sealed record GeneratedManifestFile(
        string Path,
        byte[] Content,
        string ContentType);

    private sealed record LegacyBridgeMethodSignature(
        string Name,
        string Descriptor);

    private sealed record MethodIndex(
        ushort NameIndex,
        ushort DescriptorIndex,
        string Descriptor);

    private sealed class ConstantPoolBuilder
    {
        private readonly List<ConstantPoolEntry> _entries = [];
        private readonly Dictionary<string, ushort> _utf8 = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ushort> _classes = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Name, string Descriptor), ushort> _nameAndTypes = [];
        private readonly Dictionary<(string Class, string Name, string Descriptor), ushort> _methodRefs = [];

        public ushort Count => checked((ushort)(_entries.Count + 1));

        public ushort AddUtf8(string value)
        {
            if (_utf8.TryGetValue(value, out var index))
            {
                return index;
            }

            index = AddEntry(new ConstantPoolEntry(1, value, 0, 0));
            _utf8[value] = index;
            return index;
        }

        public ushort AddClass(string internalName)
        {
            if (_classes.TryGetValue(internalName, out var index))
            {
                return index;
            }

            var nameIndex = AddUtf8(internalName);
            index = AddEntry(new ConstantPoolEntry(7, string.Empty, nameIndex, 0));
            _classes[internalName] = index;
            return index;
        }

        public ushort AddNameAndType(string name, string descriptor)
        {
            var key = (name, descriptor);
            if (_nameAndTypes.TryGetValue(key, out var index))
            {
                return index;
            }

            var nameIndex = AddUtf8(name);
            var descriptorIndex = AddUtf8(descriptor);
            index = AddEntry(new ConstantPoolEntry(12, string.Empty, nameIndex, descriptorIndex));
            _nameAndTypes[key] = index;
            return index;
        }

        public ushort AddMethodRef(string classInternalName, string name, string descriptor)
        {
            var key = (classInternalName, name, descriptor);
            if (_methodRefs.TryGetValue(key, out var index))
            {
                return index;
            }

            var classIndex = AddClass(classInternalName);
            var nameAndTypeIndex = AddNameAndType(name, descriptor);
            index = AddEntry(new ConstantPoolEntry(10, string.Empty, classIndex, nameAndTypeIndex));
            _methodRefs[key] = index;
            return index;
        }

        public void WriteTo(Stream stream)
        {
            foreach (var entry in _entries)
            {
                stream.WriteByte(entry.Tag);
                switch (entry.Tag)
                {
                    case 1:
                    {
                        var bytes = Encoding.UTF8.GetBytes(entry.Utf8Value);
                        WriteU2(stream, checked((ushort)bytes.Length));
                        stream.Write(bytes, 0, bytes.Length);
                        break;
                    }
                    case 7:
                        WriteU2(stream, entry.FirstIndex);
                        break;
                    case 10:
                    case 12:
                        WriteU2(stream, entry.FirstIndex);
                        WriteU2(stream, entry.SecondIndex);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported constant pool tag {entry.Tag}.");
                }
            }
        }

        private ushort AddEntry(ConstantPoolEntry entry)
        {
            _entries.Add(entry);
            return checked((ushort)_entries.Count);
        }
    }

    private sealed record ConstantPoolEntry(
        byte Tag,
        string Utf8Value,
        ushort FirstIndex,
        ushort SecondIndex);

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
            ".jar2" => "application/java-archive",
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
