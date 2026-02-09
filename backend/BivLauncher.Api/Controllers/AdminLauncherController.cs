using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/launcher")]
public sealed class AdminLauncherController(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IObjectStorageService objectStorageService,
    IAssetUrlService assetUrlService,
    ILauncherUpdateConfigProvider launcherUpdateConfigProvider,
    IAdminAuditService auditService,
    ILogger<AdminLauncherController> logger) : ControllerBase
{
    private sealed record RuntimeBuildResult(
        string RuntimeIdentifier,
        string ArchiveName,
        string ArchivePath,
        string PublishDirectory,
        int ExitCode,
        string Stdout,
        string Stderr,
        long SizeBytes);

    private static readonly HashSet<string> AllowedRuntimeIdentifiers =
    [
        "win-x64",
        "win-arm64",
        "linux-x64",
        "osx-x64",
        "osx-arm64"
    ];

    private static readonly HashSet<string> AllowedConfigurations =
    [
        "Release",
        "Debug"
    ];

    private static readonly Regex VersionPattern = new("^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$", RegexOptions.Compiled);
    private const int MaxLogLength = 4000;
    private const string ServerLauncherJarStorageKey = "uploads/assets/launcher.jar";

    [HttpGet("server-jar")]
    public async Task<IActionResult> GetServerLauncherJarStatus(CancellationToken cancellationToken = default)
    {
        var metadata = await objectStorageService.GetMetadataAsync(ServerLauncherJarStorageKey, cancellationToken);
        if (metadata is null)
        {
            return Ok(new
            {
                exists = false,
                key = ServerLauncherJarStorageKey,
                publicUrl = assetUrlService.BuildPublicUrl(ServerLauncherJarStorageKey),
                sizeBytes = 0L,
                contentType = string.Empty,
                sha256 = string.Empty
            });
        }

        return Ok(new
        {
            exists = true,
            key = ServerLauncherJarStorageKey,
            publicUrl = assetUrlService.BuildPublicUrl(ServerLauncherJarStorageKey),
            sizeBytes = metadata.SizeBytes,
            contentType = metadata.ContentType,
            sha256 = metadata.Sha256
        });
    }

    [HttpPost("build")]
    public async Task<IActionResult> BuildAndDownload(
        [FromBody] LauncherBuildRequest? request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request ?? new LauncherBuildRequest());
        var runtimeIdentifiers = ResolveRuntimeIdentifiers(normalized);
        var unsupportedRuntimeIdentifiers = runtimeIdentifiers
            .Where(runtimeIdentifier => !AllowedRuntimeIdentifiers.Contains(runtimeIdentifier))
            .ToList();
        if (unsupportedRuntimeIdentifiers.Count > 0)
        {
            return BadRequest(new
            {
                error = $"Unsupported runtime identifier(s): {string.Join(", ", unsupportedRuntimeIdentifiers)}. Allowed: {string.Join(", ", AllowedRuntimeIdentifiers)}."
            });
        }

        if (normalized.AutoPublishUpdate && runtimeIdentifiers.Count != 1)
        {
            return BadRequest(new
            {
                error = "Auto publish is only supported for single-runtime launcher builds. Disable auto publish or choose one runtime."
            });
        }

        if (!AllowedConfigurations.Contains(normalized.Configuration))
        {
            return BadRequest(new
            {
                error = $"Unsupported configuration '{normalized.Configuration}'. Allowed: {string.Join(", ", AllowedConfigurations)}."
            });
        }

        if (!string.IsNullOrWhiteSpace(normalized.Version) && !VersionPattern.IsMatch(normalized.Version))
        {
            return BadRequest(new
            {
                error = "Version must match pattern ^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$."
            });
        }

        var projectPath = ResolveProjectPath();
        if (!System.IO.File.Exists(projectPath))
        {
            return BadRequest(new
            {
                error = $"Launcher project file not found. Checked path: {projectPath}"
            });
        }

        var timeoutSeconds = ResolveTimeoutSeconds();
        var startedAt = DateTime.UtcNow;

        var buildRoot = ResolveBuildRoot();
        Directory.CreateDirectory(buildRoot);

        var buildSession = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
        var workingDirectory = Path.Combine(buildRoot, buildSession);
        var publishRootDirectory = Path.Combine(workingDirectory, "publish");
        Directory.CreateDirectory(publishRootDirectory);

        var defaultArchiveName = runtimeIdentifiers.Count == 1
            ? BuildArchiveName(normalized, runtimeIdentifiers[0])
            : BuildBundleArchiveName(normalized);
        var runtimeBuildResults = new List<RuntimeBuildResult>();
        string stderr = string.Empty;
        var exitCode = -1;

        try
        {
            foreach (var runtimeIdentifier in runtimeIdentifiers)
            {
                var publishDirectory = Path.Combine(publishRootDirectory, runtimeIdentifier);
                Directory.CreateDirectory(publishDirectory);
                var runtimeArchiveName = BuildArchiveName(normalized, runtimeIdentifier);
                var runtimeArchivePath = Path.Combine(workingDirectory, runtimeArchiveName);
                var runtimeStdout = string.Empty;
                var runtimeStderr = string.Empty;

                using var process = CreatePublishProcess(projectPath, publishDirectory, normalized, runtimeIdentifier);
                if (!process.Start())
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to start dotnet publish process." });
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryKillProcess(process);
                    runtimeStdout = await stdoutTask;
                    runtimeStderr = await stderrTask;
                    runtimeStderr = string.IsNullOrWhiteSpace(runtimeStderr)
                        ? $"Timed out after {timeoutSeconds} seconds."
                        : $"{runtimeStderr}{Environment.NewLine}Timed out after {timeoutSeconds} seconds.";
                    runtimeBuildResults.Add(new RuntimeBuildResult(
                        RuntimeIdentifier: runtimeIdentifier,
                        ArchiveName: runtimeArchiveName,
                        ArchivePath: runtimeArchivePath,
                        PublishDirectory: publishDirectory,
                        ExitCode: exitCode,
                        Stdout: runtimeStdout,
                        Stderr: runtimeStderr,
                        SizeBytes: 0));
                    await WriteAuditAsync(
                        action: "launcher.build.failed",
                        normalized,
                        runtimeArchiveName,
                        0,
                        startedAt,
                        exitCode,
                        BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: false),
                        BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: true),
                        runtimeBuildResults,
                        cancellationToken);
                    return StatusCode(StatusCodes.Status504GatewayTimeout, new
                    {
                        error = $"Launcher build timed out after {timeoutSeconds} seconds for runtime '{runtimeIdentifier}'.",
                        runtimeIdentifier
                    });
                }

                runtimeStdout = await stdoutTask;
                runtimeStderr = await stderrTask;
                exitCode = process.ExitCode;

                if (exitCode != 0)
                {
                    runtimeBuildResults.Add(new RuntimeBuildResult(
                        RuntimeIdentifier: runtimeIdentifier,
                        ArchiveName: runtimeArchiveName,
                        ArchivePath: runtimeArchivePath,
                        PublishDirectory: publishDirectory,
                        ExitCode: exitCode,
                        Stdout: runtimeStdout,
                        Stderr: runtimeStderr,
                        SizeBytes: 0));
                    await WriteAuditAsync(
                        action: "launcher.build.failed",
                        normalized,
                        runtimeArchiveName,
                        0,
                        startedAt,
                        exitCode,
                        BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: false),
                        BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: true),
                        runtimeBuildResults,
                        cancellationToken);
                    return BadRequest(new
                    {
                        error = $"Launcher build failed for runtime '{runtimeIdentifier}'. Check output details.",
                        runtimeIdentifier,
                        exitCode,
                        stdout = Truncate(runtimeStdout, MaxLogLength),
                        stderr = Truncate(runtimeStderr, MaxLogLength)
                    });
                }

                ZipFile.CreateFromDirectory(publishDirectory, runtimeArchivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
                var runtimeSizeBytes = new FileInfo(runtimeArchivePath).Length;
                runtimeBuildResults.Add(new RuntimeBuildResult(
                    RuntimeIdentifier: runtimeIdentifier,
                    ArchiveName: runtimeArchiveName,
                    ArchivePath: runtimeArchivePath,
                    PublishDirectory: publishDirectory,
                    ExitCode: exitCode,
                    Stdout: runtimeStdout,
                    Stderr: runtimeStderr,
                    SizeBytes: runtimeSizeBytes));
            }

            if (runtimeBuildResults.Count == 1)
            {
                var singleRuntimeResult = runtimeBuildResults[0];
                var fileBytes = await System.IO.File.ReadAllBytesAsync(singleRuntimeResult.ArchivePath, cancellationToken);
                var autoPublishWarning = string.Empty;

                if (normalized.AutoPublishUpdate)
                {
                    try
                    {
                        await AutoPublishLauncherUpdateAsync(
                            normalized,
                            singleRuntimeResult.ArchiveName,
                            fileBytes,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Launcher auto-publish failed for archive {ArchiveName}. Build ZIP will still be returned.",
                            singleRuntimeResult.ArchiveName);
                        autoPublishWarning = $"Launcher auto-publish warning: {ex.Message}";
                    }
                }

                if (!string.IsNullOrWhiteSpace(autoPublishWarning))
                {
                    var nextStderr = string.IsNullOrWhiteSpace(singleRuntimeResult.Stderr)
                        ? autoPublishWarning
                        : $"{singleRuntimeResult.Stderr}{Environment.NewLine}{autoPublishWarning}";
                    singleRuntimeResult = singleRuntimeResult with { Stderr = nextStderr };
                    runtimeBuildResults[0] = singleRuntimeResult;
                    Response.Headers.Append("X-Launcher-Update-Publish-Warning", Truncate(autoPublishWarning, 300));
                }

                await WriteAuditAsync(
                    action: "launcher.build",
                    normalized,
                    singleRuntimeResult.ArchiveName,
                    fileBytes.LongLength,
                    startedAt,
                    singleRuntimeResult.ExitCode,
                    singleRuntimeResult.Stdout,
                    singleRuntimeResult.Stderr,
                    runtimeBuildResults,
                    cancellationToken);

                return File(fileBytes, "application/zip", singleRuntimeResult.ArchiveName);
            }

            var bundleArchiveName = BuildBundleArchiveName(normalized);
            var bundleArchivePath = Path.Combine(workingDirectory, bundleArchiveName);
            CreateRuntimeBundleArchive(bundleArchivePath, runtimeBuildResults);
            var bundleFileBytes = await System.IO.File.ReadAllBytesAsync(bundleArchivePath, cancellationToken);

            await WriteAuditAsync(
                action: "launcher.build.multi",
                normalized,
                bundleArchiveName,
                bundleFileBytes.LongLength,
                startedAt,
                exitCode,
                BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: false),
                BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: true),
                runtimeBuildResults,
                cancellationToken);

            return File(bundleFileBytes, "application/zip", bundleArchiveName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Launcher build failed unexpectedly.");
            var errorDetails = string.IsNullOrWhiteSpace(stderr) ? ex.Message : $"{stderr}{Environment.NewLine}{ex.Message}";
            await WriteAuditAsync(
                action: "launcher.build.failed",
                normalized,
                defaultArchiveName,
                0,
                startedAt,
                exitCode,
                BuildCombinedRuntimeLog(runtimeBuildResults, includeStdErr: false),
                errorDetails,
                runtimeBuildResults,
                cancellationToken);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected launcher build failure.", details = ex.Message });
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private Process CreatePublishProcess(
        string projectPath,
        string publishDirectory,
        LauncherBuildRequest request,
        string runtimeIdentifier)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? environment.ContentRootPath
        };

        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(request.Configuration);
        startInfo.ArgumentList.Add("--runtime");
        startInfo.ArgumentList.Add(runtimeIdentifier);
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add(request.SelfContained ? "true" : "false");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(publishDirectory);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("/p:DebugType=None");
        startInfo.ArgumentList.Add("/p:DebugSymbols=false");
        startInfo.ArgumentList.Add($"/p:PublishSingleFile={(request.PublishSingleFile ? "true" : "false")}");

        if (request.PublishSingleFile)
        {
            startInfo.ArgumentList.Add("/p:IncludeNativeLibrariesForSelfExtract=true");
        }

        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            startInfo.ArgumentList.Add($"/p:Version={request.Version}");
        }

        var launcherApiBaseUrl = ResolveLauncherDefaultApiBaseUrl();
        if (!string.IsNullOrWhiteSpace(launcherApiBaseUrl))
        {
            startInfo.ArgumentList.Add($"/p:BivLauncherApiBaseUrl={launcherApiBaseUrl}");
        }

        return new Process
        {
            StartInfo = startInfo
        };
    }

    private async Task WriteAuditAsync(
        string action,
        LauncherBuildRequest request,
        string archiveName,
        long sizeBytes,
        DateTime startedAt,
        int exitCode,
        string stdout,
        string stderr,
        IReadOnlyList<RuntimeBuildResult>? runtimeBuildResults,
        CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: action,
            actor: actor,
            entityType: "launcher",
            entityId: archiveName,
            details: new
            {
                request.RuntimeIdentifier,
                request.RuntimeIdentifiers,
                request.Configuration,
                request.SelfContained,
                request.PublishSingleFile,
                request.Version,
                request.AutoPublishUpdate,
                request.ReleaseNotes,
                archiveName,
                sizeBytes,
                startedAtUtc = startedAt,
                finishedAtUtc = DateTime.UtcNow,
                durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                exitCode,
                stdout = Truncate(stdout, MaxLogLength),
                stderr = Truncate(stderr, MaxLogLength),
                runtimeResults = runtimeBuildResults?.Select(result => new
                {
                    result.RuntimeIdentifier,
                    result.ArchiveName,
                    result.SizeBytes,
                    result.ExitCode,
                    stdout = Truncate(result.Stdout, MaxLogLength),
                    stderr = Truncate(result.Stderr, MaxLogLength)
                }).ToList() ?? []
            },
            cancellationToken: cancellationToken);
    }

    private static LauncherBuildRequest NormalizeRequest(LauncherBuildRequest request)
    {
        var runtime = (request.RuntimeIdentifier ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(runtime))
        {
            runtime = "win-x64";
        }

        var configuration = (request.Configuration ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(configuration))
        {
            configuration = "Release";
        }
        else if (string.Equals(configuration, "release", StringComparison.OrdinalIgnoreCase))
        {
            configuration = "Release";
        }
        else if (string.Equals(configuration, "debug", StringComparison.OrdinalIgnoreCase))
        {
            configuration = "Debug";
        }

        var runtimeIdentifiers = request.RuntimeIdentifiers
            .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (runtimeIdentifiers.Count == 0)
        {
            runtimeIdentifiers.Add(runtime);
        }
        runtime = runtimeIdentifiers[0];

        return new LauncherBuildRequest
        {
            RuntimeIdentifier = runtime,
            RuntimeIdentifiers = runtimeIdentifiers,
            Configuration = configuration,
            SelfContained = request.SelfContained,
            PublishSingleFile = request.PublishSingleFile,
            Version = (request.Version ?? string.Empty).Trim(),
            AutoPublishUpdate = request.AutoPublishUpdate,
            ReleaseNotes = (request.ReleaseNotes ?? string.Empty).Trim()
        };
    }

    private static IReadOnlyList<string> ResolveRuntimeIdentifiers(LauncherBuildRequest request)
    {
        var runtimeIdentifiers = request.RuntimeIdentifiers
            .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (runtimeIdentifiers.Count == 0)
        {
            var fallback = (request.RuntimeIdentifier ?? string.Empty).Trim().ToLowerInvariant();
            runtimeIdentifiers.Add(string.IsNullOrWhiteSpace(fallback) ? "win-x64" : fallback);
        }

        return runtimeIdentifiers;
    }

    private string ResolveProjectPath()
    {
        var configuredPath = configuration["LAUNCHER_BUILD_PROJECT_PATH"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredAbsolutePath = ToAbsolutePath(configuredPath);
            if (System.IO.File.Exists(configuredAbsolutePath))
            {
                return configuredAbsolutePath;
            }

            logger.LogWarning(
                "Configured LAUNCHER_BUILD_PROJECT_PATH does not exist: {ProjectPath}. Falling back to auto-detection.",
                configuredAbsolutePath);
        }

        var candidates = new[]
        {
            "../../launcher/BivLauncher.Client/BivLauncher.Client.csproj",
            "../launcher/BivLauncher.Client/BivLauncher.Client.csproj",
            "launcher/BivLauncher.Client/BivLauncher.Client.csproj",
            "/workspace/launcher/BivLauncher.Client/BivLauncher.Client.csproj",
            "/app/launcher/BivLauncher.Client/BivLauncher.Client.csproj"
        };

        foreach (var candidate in candidates)
        {
            var absolutePath = ToAbsolutePath(candidate);
            if (System.IO.File.Exists(absolutePath))
            {
                return absolutePath;
            }
        }

        return ToAbsolutePath(candidates[0]);
    }

    private string ResolveBuildRoot()
    {
        var configuredPath = configuration["LAUNCHER_BUILD_OUTPUT_ROOT"] ?? Path.Combine(Path.GetTempPath(), "bivlauncher-launcher-builds");
        return ToAbsolutePath(configuredPath);
    }

    private string ResolveLauncherDefaultApiBaseUrl()
    {
        var configuredUrl = configuration["PUBLIC_BASE_URL"] ?? configuration["PublicBaseUrl"];
        return string.IsNullOrWhiteSpace(configuredUrl)
            ? string.Empty
            : configuredUrl.Trim().TrimEnd('/');
    }

    private int ResolveTimeoutSeconds()
    {
        var configured = configuration["LAUNCHER_BUILD_TIMEOUT_SECONDS"];
        if (!int.TryParse(configured, out var seconds))
        {
            return 1200;
        }

        return Math.Clamp(seconds, 60, 3600);
    }

    private static string BuildArchiveName(LauncherBuildRequest request, string runtimeIdentifier)
    {
        var version = string.IsNullOrWhiteSpace(request.Version) ? "dev" : request.Version;
        return $"launcher-{version}-{runtimeIdentifier}.zip";
    }

    private static string BuildBundleArchiveName(LauncherBuildRequest request)
    {
        var version = string.IsNullOrWhiteSpace(request.Version) ? "dev" : request.Version;
        return $"launcher-{version}-multi.zip";
    }

    private static void CreateRuntimeBundleArchive(string bundleArchivePath, IReadOnlyList<RuntimeBuildResult> runtimeBuildResults)
    {
        using var stream = System.IO.File.Create(bundleArchivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var runtimeBuildResult in runtimeBuildResults)
        {
            archive.CreateEntryFromFile(
                sourceFileName: runtimeBuildResult.ArchivePath,
                entryName: runtimeBuildResult.ArchiveName,
                compressionLevel: CompressionLevel.Optimal);
        }
    }

    private static string BuildCombinedRuntimeLog(IReadOnlyList<RuntimeBuildResult> runtimeBuildResults, bool includeStdErr)
    {
        if (runtimeBuildResults.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var runtimeBuildResult in runtimeBuildResults)
        {
            var payload = includeStdErr ? runtimeBuildResult.Stderr : runtimeBuildResult.Stdout;
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append('[');
            builder.Append(runtimeBuildResult.RuntimeIdentifier);
            builder.AppendLine("]");
            builder.Append(payload.Trim());
        }

        return builder.ToString();
    }

    private async Task AutoPublishLauncherUpdateAsync(
        LauncherBuildRequest request,
        string archiveName,
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        if (archiveBytes.Length <= 0)
        {
            throw new InvalidOperationException("Launcher archive is empty and cannot be auto-published.");
        }

        var version = ResolveUpdateVersion(request.Version);
        var storageKey = $"launcher-updates/{version}/{archiveName}";
        await using var stream = new MemoryStream(archiveBytes, writable: false);
        await objectStorageService.UploadAsync(
            storageKey,
            stream,
            contentType: "application/zip",
            cancellationToken: cancellationToken);

        var publicUrl = assetUrlService.BuildPublicUrl(storageKey);
        var releaseNotes = string.IsNullOrWhiteSpace(request.ReleaseNotes)
            ? $"Automated launcher build {version}"
            : request.ReleaseNotes.Trim();

        await launcherUpdateConfigProvider.SaveAsync(
            new LauncherUpdateConfig(
                LatestVersion: version,
                DownloadUrl: publicUrl,
                ReleaseNotes: releaseNotes),
            cancellationToken);
    }

    private static string ResolveUpdateVersion(string requestVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestVersion))
        {
            return requestVersion.Trim();
        }

        return DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength]}...";
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures for temporary build directories.
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Process may already be gone.
        }
    }

    private string ToAbsolutePath(string inputPath)
    {
        var candidate = (inputPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = ".";
        }

        return Path.GetFullPath(
            Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(environment.ContentRootPath, candidate));
    }
}
