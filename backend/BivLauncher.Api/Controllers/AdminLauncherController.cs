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
    IHttpClientFactory httpClientFactory,
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
    private const string DefaultServerLauncherJarVersion = "1.2.7";
    private const long DefaultServerLauncherJarMaxBytes = 32L * 1024 * 1024;
    private const string LegacySessionDomainSource = "authserver.mojang.com";
    private const string LegacySessionDomainTarget = "session.minecraft.net";

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
                publicUrl = string.Empty,
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

    [HttpPost("server-jar/build")]
    public async Task<IActionResult> BuildServerLauncherJar(
        [FromBody] ServerLauncherJarBuildRequest? request,
        CancellationToken cancellationToken = default)
    {
        var resolvedVersion = ResolveServerLauncherJarVersion(request?.Version);
        if (!VersionPattern.IsMatch(resolvedVersion))
        {
            return BadRequest(new
            {
                error = "Server launcher.jar version must match pattern ^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$."
            });
        }

        var sourceUrl = ResolveServerLauncherJarSourceUrl(resolvedVersion, request?.SourceUrl);
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri) ||
            (sourceUri.Scheme != Uri.UriSchemeHttps && sourceUri.Scheme != Uri.UriSchemeHttp))
        {
            return BadRequest(new
            {
                error = "Server launcher.jar source URL must be an absolute http/https URL."
            });
        }

        var timeoutSeconds = ResolveServerLauncherJarDownloadTimeoutSeconds();
        var maxBytes = ResolveServerLauncherJarMaxBytes();
        var startedAt = DateTime.UtcNow;
        byte[] payload;
        string contentType;
        int responseStatusCode;

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var response = await client.GetAsync(
                sourceUri,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            responseStatusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return BadRequest(new
                {
                    error = $"Server launcher.jar download failed with HTTP {(int)response.StatusCode}.",
                    sourceUrl
                });
            }

            contentType = string.IsNullOrWhiteSpace(response.Content.Headers.ContentType?.MediaType)
                ? "application/java-archive"
                : response.Content.Headers.ContentType!.MediaType!;

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            payload = await ReadWithSizeLimitAsync(stream, maxBytes, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = $"Server launcher.jar download timed out after {timeoutSeconds} seconds.",
                sourceUrl
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message,
                sourceUrl
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download server launcher.jar from {SourceUrl}.", sourceUrl);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Failed to download server launcher.jar from upstream source.",
                sourceUrl
            });
        }

        if (!LooksLikeJarArchive(payload))
        {
            return BadRequest(new
            {
                error = "Downloaded file is not a valid JAR/ZIP archive.",
                sourceUrl
            });
        }

        var legacySessionPatchStats = PatchLegacySessionDomainMapping(payload);
        payload = legacySessionPatchStats.Payload;

        await using (var uploadStream = new MemoryStream(payload, writable: false))
        {
            await objectStorageService.UploadAsync(
                ServerLauncherJarStorageKey,
                uploadStream,
                string.IsNullOrWhiteSpace(contentType) ? "application/java-archive" : contentType,
                cancellationToken: cancellationToken);
        }

        var metadata = await objectStorageService.GetMetadataAsync(ServerLauncherJarStorageKey, cancellationToken);
        await auditService.WriteAsync(
            action: "launcher.server-jar.build",
            actor: User.Identity?.Name ?? "admin",
            entityType: "launcher",
            entityId: ServerLauncherJarStorageKey,
            details: new
            {
                sourceUrl,
                version = resolvedVersion,
                timeoutSeconds,
                maxBytes,
                downloadedSizeBytes = payload.LongLength,
                upstreamStatusCode = responseStatusCode,
                legacySessionPatchEnabled = true,
                legacySessionPatchClassEntriesTouched = legacySessionPatchStats.ClassEntriesTouched,
                legacySessionPatchStringReplacements = legacySessionPatchStats.StringReplacements,
                finishedAtUtc = DateTime.UtcNow,
                durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                storageSizeBytes = metadata?.SizeBytes ?? payload.LongLength,
                storageContentType = metadata?.ContentType ?? contentType,
                storageSha256 = metadata?.Sha256 ?? string.Empty
            },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            exists = true,
            key = ServerLauncherJarStorageKey,
            publicUrl = assetUrlService.BuildPublicUrl(ServerLauncherJarStorageKey),
            sizeBytes = metadata?.SizeBytes ?? payload.LongLength,
            contentType = metadata?.ContentType ?? contentType,
            sha256 = metadata?.Sha256 ?? string.Empty,
            sourceUrl,
            version = resolvedVersion,
            legacySessionPatchEnabled = true,
            legacySessionPatchClassEntriesTouched = legacySessionPatchStats.ClassEntriesTouched,
            legacySessionPatchStringReplacements = legacySessionPatchStats.StringReplacements
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

    private string ResolveServerLauncherJarVersion(string? requestVersion)
    {
        var explicitVersion = (requestVersion ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(explicitVersion))
        {
            return explicitVersion;
        }

        var configuredVersion = (configuration["LAUNCHER_SERVER_JAR_VERSION"] ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(configuredVersion)
            ? DefaultServerLauncherJarVersion
            : configuredVersion;
    }

    private string ResolveServerLauncherJarSourceUrl(string resolvedVersion, string? requestSourceUrl)
    {
        var explicitSourceUrl = (requestSourceUrl ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(explicitSourceUrl))
        {
            return explicitSourceUrl;
        }

        var configuredSourceUrl = (configuration["LAUNCHER_SERVER_JAR_URL"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredSourceUrl))
        {
            return configuredSourceUrl;
        }

        return $"https://repo1.maven.org/maven2/org/glavo/hmcl/authlib-injector/{resolvedVersion}/authlib-injector-{resolvedVersion}.jar";
    }

    private int ResolveServerLauncherJarDownloadTimeoutSeconds()
    {
        var raw = configuration["LAUNCHER_SERVER_JAR_DOWNLOAD_TIMEOUT_SECONDS"];
        if (!int.TryParse(raw, out var seconds))
        {
            return 120;
        }

        return Math.Clamp(seconds, 15, 1800);
    }

    private long ResolveServerLauncherJarMaxBytes()
    {
        var raw = configuration["LAUNCHER_SERVER_JAR_MAX_BYTES"];
        if (!long.TryParse(raw, out var maxBytes))
        {
            return DefaultServerLauncherJarMaxBytes;
        }

        return Math.Clamp(maxBytes, 256 * 1024, 256L * 1024 * 1024);
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

    private static async Task<byte[]> ReadWithSizeLimitAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                throw new InvalidOperationException($"Downloaded server launcher.jar exceeds size limit ({maxBytes} bytes).");
            }
        }

        if (buffer.Length <= 0)
        {
            throw new InvalidOperationException("Downloaded server launcher.jar is empty.");
        }

        return buffer.ToArray();
    }

    private static bool LooksLikeJarArchive(byte[] payload)
    {
        if (payload.Length < 4)
        {
            return false;
        }

        return payload[0] == (byte)'P' &&
               payload[1] == (byte)'K' &&
               (payload[2] == 3 || payload[2] == 5 || payload[2] == 7) &&
               (payload[3] == 4 || payload[3] == 6 || payload[3] == 8);
    }

    private static LegacySessionPatchStats PatchLegacySessionDomainMapping(byte[] payload)
    {
        if (payload.Length <= 0)
        {
            return new LegacySessionPatchStats(payload, 0, 0);
        }

        using var input = new MemoryStream(payload, writable: false);
        using var output = new MemoryStream(capacity: payload.Length + 1024);
        var classEntriesTouched = 0;
        var stringReplacements = 0;
        using (var inputArchive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
        using (var outputArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inputArchive.Entries)
            {
                var target = outputArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                target.LastWriteTime = entry.LastWriteTime;

                using var sourceStream = entry.Open();
                using var destinationStream = target.Open();
                if (!entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                {
                    sourceStream.CopyTo(destinationStream);
                    continue;
                }

                using var classBuffer = new MemoryStream();
                sourceStream.CopyTo(classBuffer);
                var classBytes = classBuffer.ToArray();
                if (classBytes.Length <= 0)
                {
                    continue;
                }

                var replacements = ReplaceAsciiSequenceInPlace(
                    classBytes,
                    LegacySessionDomainSource,
                    LegacySessionDomainTarget);

                if (replacements > 0)
                {
                    classEntriesTouched++;
                    stringReplacements += replacements;
                }

                destinationStream.Write(classBytes, 0, classBytes.Length);
            }
        }

        return new LegacySessionPatchStats(output.ToArray(), classEntriesTouched, stringReplacements);
    }

    private static int ReplaceAsciiSequenceInPlace(byte[] payload, string source, string target)
    {
        if (payload.Length == 0 || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            return 0;
        }

        var sourceBytes = Encoding.ASCII.GetBytes(source);
        var targetBytes = Encoding.ASCII.GetBytes(target);
        if (sourceBytes.Length != targetBytes.Length || sourceBytes.Length == 0 || payload.Length < sourceBytes.Length)
        {
            return 0;
        }

        var replacements = 0;
        for (var index = 0; index <= payload.Length - sourceBytes.Length; index++)
        {
            var matched = true;
            for (var offset = 0; offset < sourceBytes.Length; offset++)
            {
                if (payload[index + offset] == sourceBytes[offset])
                {
                    continue;
                }

                matched = false;
                break;
            }

            if (!matched)
            {
                continue;
            }

            Buffer.BlockCopy(targetBytes, 0, payload, index, targetBytes.Length);
            replacements++;
            index += sourceBytes.Length - 1;
        }

        return replacements;
    }

    private sealed record LegacySessionPatchStats(
        byte[] Payload,
        int ClassEntriesTouched,
        int StringReplacements);

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
