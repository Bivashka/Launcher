using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace BivLauncher.Client.Services;

public sealed class LauncherUpdateService(
    ISettingsService settingsService,
    ILogService logService) : ILauncherUpdateService
{
    private static readonly TimeSpan DownloadCandidateHeaderTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DownloadCandidateReadTimeout = TimeSpan.FromSeconds(20);
    private const string LauncherApiBaseUrlEnvVar = "BIVLAUNCHER_API_BASE_URL";
    private const string LauncherApiBaseUrlRuEnvVar = "BIVLAUNCHER_API_BASE_URL_RU";
    private const string LauncherApiBaseUrlEuEnvVar = "BIVLAUNCHER_API_BASE_URL_EU";
    private const string LauncherApiBaseUrlAssemblyMetadataKey = "BivLauncher.ApiBaseUrl";
    private const string LauncherApiBaseUrlRuAssemblyMetadataKey = "BivLauncher.ApiBaseUrlRu";
    private const string LauncherApiBaseUrlEuAssemblyMetadataKey = "BivLauncher.ApiBaseUrlEu";
    private const string LauncherFallbackApiBaseUrlsAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls";
    private const string LauncherFallbackApiBaseUrlsRuAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls.Ru";
    private const string LauncherFallbackApiBaseUrlsEuAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls.Eu";

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(20)
    };

    public async Task<string> DownloadPackageAsync(
        string downloadUrl,
        string latestVersion,
        IProgress<LauncherUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = downloadUrl.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            throw new InvalidOperationException("Update download URL is empty.");
        }

        var version = string.IsNullOrWhiteSpace(latestVersion) ? "unknown" : SanitizePathPart(latestVersion);
        var versionDirectory = Path.Combine(settingsService.GetUpdatesDirectory(), version);
        Directory.CreateDirectory(versionDirectory);
        Exception? lastError = null;
        var packagePath = Path.Combine(versionDirectory, $"launcher-update-{version}.zip");

        foreach (var candidateUrl in await ResolveDownloadCandidatesAsync(normalizedUrl, cancellationToken))
        {
            Exception? candidateError = null;
            try
            {
                using var response = await OpenDownloadResponseAsync(candidateUrl, cancellationToken);
                var resolvedDownloadUrl = response.RequestMessage?.RequestUri?.ToString() ?? candidateUrl;
                var extension = ResolvePackageExtension(resolvedDownloadUrl, response.Content.Headers);
                if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Update package must be a .zip archive.");
                }

                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await CopyDownloadStreamAsync(contentStream, fileStream, response.Content.Headers.ContentLength, progress, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);

                if (!string.Equals(candidateUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    logService.LogInfo($"Launcher update download switched to fallback URL: {candidateUrl}");
                }

                logService.LogInfo($"Launcher update package downloaded: {packagePath}");
                return packagePath;
            }
            catch (HttpRequestException ex)
            {
                candidateError = ex;
                lastError = ex;
                logService.LogInfo($"Launcher update download candidate failed and will be skipped: {candidateUrl} ({ex.Message})");
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                candidateError = ex;
                lastError = ex;
                logService.LogInfo($"Launcher update download candidate timed out and will be skipped: {candidateUrl} ({ex.Message})");
            }
            catch (InvalidOperationException ex)
            {
                candidateError = ex;
                lastError = ex;
                logService.LogInfo($"Launcher update download candidate failed and will be skipped: {candidateUrl} ({ex.Message})");
            }
            finally
            {
                if (candidateError is not null && File.Exists(packagePath))
                {
                    try
                    {
                        File.Delete(packagePath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        throw lastError ?? new InvalidOperationException("No reachable launcher update download URL is available.");
    }

    private async Task<HttpResponseMessage> OpenDownloadResponseAsync(string candidateUrl, CancellationToken cancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(DownloadCandidateHeaderTimeout);
        var response = await _httpClient.GetAsync(
            candidateUrl,
            HttpCompletionOption.ResponseHeadersRead,
            requestCts.Token);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        if (!ShouldTryNextDownloadLocation(response.StatusCode))
        {
            response.EnsureSuccessStatusCode();
        }

        var statusCode = response.StatusCode;
        var reasonPhrase = response.ReasonPhrase;
        response.Dispose();
        throw new HttpRequestException(
            $"Response status code does not indicate success: {(int)statusCode} ({reasonPhrase}).",
            null,
            statusCode);
    }

    private async Task CopyDownloadStreamAsync(
        Stream contentStream,
        FileStream fileStream,
        long? totalBytes,
        IProgress<LauncherUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 128];
        long downloadedBytes = 0;

        while (true)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(DownloadCandidateReadTimeout);
            var read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
            if (read <= 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;
            progress?.Report(new LauncherUpdateDownloadProgress(downloadedBytes, totalBytes));
        }
    }

    public void ScheduleInstallAndRestart(string packagePath, string executablePath)
    {
        var normalizedPackagePath = packagePath.Trim();
        if (!File.Exists(normalizedPackagePath))
        {
            throw new FileNotFoundException("Downloaded update package not found.", normalizedPackagePath);
        }

        if (!string.Equals(Path.GetExtension(normalizedPackagePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded update package is not a .zip file.");
        }

        var normalizedExecutablePath = executablePath.Trim();
        if (!File.Exists(normalizedExecutablePath))
        {
            throw new FileNotFoundException("Launcher executable path not found.", normalizedExecutablePath);
        }

        var targetDirectory = (Path.GetDirectoryName(normalizedExecutablePath) ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var updateDirectory = Path.Combine(settingsService.GetUpdatesDirectory(), "runtime");
        Directory.CreateDirectory(updateDirectory);

        var scriptPath = Path.Combine(updateDirectory, "apply-update.ps1");
        File.WriteAllText(scriptPath, BuildPowerShellScript(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var arguments = string.Join(' ', new[]
        {
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", Quote(scriptPath),
            "-TargetDir", Quote(targetDirectory),
            "-PackagePath", Quote(normalizedPackagePath),
            "-ProcessId", Environment.ProcessId.ToString(),
            "-ExePath", Quote(normalizedExecutablePath)
        });

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updateDirectory
        };

        Process.Start(startInfo);
        logService.LogInfo($"Launcher update installer scheduled. Script: {scriptPath}");
    }

    private static string ResolvePackageExtension(string downloadUrl, HttpContentHeaders headers)
    {
        var fromContentDisposition = headers.ContentDisposition?.FileNameStar
            ?? headers.ContentDisposition?.FileName;
        var candidate = TrimFileNameQuotes(fromContentDisposition);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var ext = Path.GetExtension(candidate);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                return ext.ToLowerInvariant();
            }
        }

        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                return ext.ToLowerInvariant();
            }
        }

        var mediaType = headers.ContentType?.MediaType ?? string.Empty;
        return mediaType.Contains("zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : string.Empty;
    }

    private static string TrimFileNameQuotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Trim('"');
    }

    private static string SanitizePathPart(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private async Task<IReadOnlyList<string>> ResolveDownloadCandidatesAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        void Add(string? value)
        {
            var normalized = NormalizeBaseUrlOrEmpty(value);
            if (string.IsNullOrWhiteSpace(normalized) ||
                candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            candidates.Add(normalized);
        }

        if (TryResolvePublicAssetPath(downloadUrl, out var publicAssetPath))
        {
            var settings = await settingsService.LoadAsync(cancellationToken);
            var selectedRegionCode = NormalizeApiRegionCode(settings.PreferredApiRegion);
            if (!string.IsNullOrWhiteSpace(selectedRegionCode))
            {
                Add(ResolveRegionalApiBaseUrl(selectedRegionCode));
                if (IsApiBaseUrlAllowedForRegion(settings.ApiBaseUrl, selectedRegionCode))
                {
                    Add(settings.ApiBaseUrl);
                }

                if (IsApiBaseUrlAllowedForRegion(settings.PlayerAuthApiBaseUrl, selectedRegionCode))
                {
                    Add(settings.PlayerAuthApiBaseUrl);
                }

                foreach (var knownApiBaseUrl in settings.KnownApiBaseUrls ?? [])
                {
                    if (IsApiBaseUrlAllowedForRegion(knownApiBaseUrl, selectedRegionCode))
                    {
                        Add(knownApiBaseUrl);
                    }
                }

                foreach (var bundledFallback in ResolveBundledFallbackApiBaseUrls(selectedRegionCode))
                {
                    Add(bundledFallback);
                }
            }
            else
            {
                Add(settings.ApiBaseUrl);
                Add(settings.PlayerAuthApiBaseUrl);

                foreach (var knownApiBaseUrl in settings.KnownApiBaseUrls ?? [])
                {
                    Add(knownApiBaseUrl);
                }

                Add(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEnvVar));
                Add(Environment.GetEnvironmentVariable(LauncherApiBaseUrlRuEnvVar));
                Add(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEuEnvVar));
                Add(ResolveAssemblyMetadata(LauncherApiBaseUrlAssemblyMetadataKey));
                Add(ResolveAssemblyMetadata(LauncherApiBaseUrlRuAssemblyMetadataKey));
                Add(ResolveAssemblyMetadata(LauncherApiBaseUrlEuAssemblyMetadataKey));

                foreach (var bundledFallback in ResolveBundledFallbackApiBaseUrls())
                {
                    Add(bundledFallback);
                }
            }

            var urls = new List<string>();
            foreach (var candidateBaseUrl in candidates)
            {
                urls.Add(BuildUri(candidateBaseUrl, publicAssetPath));
            }

            if (!urls.Contains(downloadUrl, StringComparer.OrdinalIgnoreCase))
            {
                urls.Insert(0, downloadUrl);
            }

            return urls;
        }

        return [downloadUrl];
    }

    private static bool TryResolvePublicAssetPath(string assetReference, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(assetReference))
        {
            return false;
        }

        if (Uri.TryCreate(assetReference, UriKind.Absolute, out var absoluteUri))
        {
            if (!absoluteUri.AbsolutePath.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = $"{absoluteUri.AbsolutePath}{absoluteUri.Query}";
            return true;
        }

        if (!assetReference.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = assetReference;
        return true;
    }

    private static string BuildUri(string apiBaseUrl, string path)
    {
        var normalizedBase = NormalizeBaseUrlOrEmpty(apiBaseUrl);
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return string.IsNullOrWhiteSpace(normalizedBase)
            ? normalizedPath
            : $"{normalizedBase}{normalizedPath}";
    }

    private static bool ShouldTryNextDownloadLocation(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.NotFound or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    private static string NormalizeBaseUrlOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string ResolveAssemblyMetadata(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        return (attribute?.Value ?? string.Empty).Trim();
    }

    private static string NormalizeApiRegionCode(string? regionCode)
    {
        return (regionCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ru" => "ru",
            "eu" => "eu",
            _ => string.Empty
        };
    }

    private static string ResolveRegionalApiBaseUrl(string regionCode)
    {
        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        if (string.IsNullOrWhiteSpace(normalizedRegionCode))
        {
            return string.Empty;
        }

        return normalizedRegionCode switch
        {
            "ru" => NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable(LauncherApiBaseUrlRuEnvVar))
                is var envRu && !string.IsNullOrWhiteSpace(envRu)
                    ? envRu
                    : NormalizeBaseUrlOrEmpty(ResolveAssemblyMetadata(LauncherApiBaseUrlRuAssemblyMetadataKey)),
            "eu" => NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEuEnvVar))
                is var envEu && !string.IsNullOrWhiteSpace(envEu)
                    ? envEu
                    : NormalizeBaseUrlOrEmpty(ResolveAssemblyMetadata(LauncherApiBaseUrlEuAssemblyMetadataKey)),
            _ => string.Empty
        };
    }

    private static bool IsApiBaseUrlAllowedForRegion(string? candidate, string regionCode)
    {
        var normalizedCandidate = NormalizeBaseUrlOrEmpty(candidate);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return false;
        }

        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        if (string.IsNullOrWhiteSpace(normalizedRegionCode))
        {
            return true;
        }

        if (BaseUrlsEqual(normalizedCandidate, ResolveRegionalApiBaseUrl(normalizedRegionCode)))
        {
            return true;
        }

        return ResolveBundledFallbackApiBaseUrls(normalizedRegionCode)
            .Contains(normalizedCandidate, StringComparer.OrdinalIgnoreCase);
    }

    private static bool BaseUrlsEqual(string? left, string? right)
    {
        return string.Equals(
            NormalizeBaseUrlOrEmpty(left),
            NormalizeBaseUrlOrEmpty(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveBundledFallbackApiBaseUrls(string? regionCode = null)
    {
        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        var metadataKey = normalizedRegionCode switch
        {
            "ru" => LauncherFallbackApiBaseUrlsRuAssemblyMetadataKey,
            "eu" => LauncherFallbackApiBaseUrlsEuAssemblyMetadataKey,
            _ => LauncherFallbackApiBaseUrlsAssemblyMetadataKey
        };
        var rawValue = ResolveAssemblyMetadata(metadataKey);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeBaseUrlOrEmpty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildPowerShellScript()
    {
        return """
param(
    [Parameter(Mandatory = $true)][string]$TargetDir,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$ExePath
)
$ErrorActionPreference = 'Stop'
$logPath = Join-Path ([System.IO.Path]::GetDirectoryName($PackagePath)) "apply-update.log"
function Write-Log {
    param([string]$Message)
    $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff")
    Add-Content -Path $logPath -Value "[$timestamp] $Message"
}
Write-Log "Update apply started. TargetDir='$TargetDir' PackagePath='$PackagePath' ExePath='$ExePath' ProcessId=$ProcessId"
$deadline = (Get-Date).AddMinutes(3)
while ($true) {
    $running = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $running) { break }
    if ((Get-Date) -gt $deadline) { break }
    Start-Sleep -Milliseconds 500
}
Write-Log "Wait loop completed."
$extractRoot = Join-Path ([System.IO.Path]::GetDirectoryName($PackagePath)) "extracted"
if (Test-Path $extractRoot) {
    Remove-Item -Path $extractRoot -Recurse -Force
    Write-Log "Previous extracted directory removed: $extractRoot"
}
Expand-Archive -Path $PackagePath -DestinationPath $extractRoot -Force
Write-Log "Archive extracted to '$extractRoot'."
$sourceRoot = $extractRoot
$expectedExeName = [System.IO.Path]::GetFileName($ExePath)
$expectedExe = Get-ChildItem -Path $extractRoot -Filter $expectedExeName -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($expectedExe) {
    $sourceRoot = $expectedExe.Directory.FullName
    Write-Log "Detected source root by matching exe: '$sourceRoot'."
}
else {
    $children = Get-ChildItem -Path $extractRoot -Force
    if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
        $sourceRoot = $children[0].FullName
        Write-Log "Detected source root by single child directory: '$sourceRoot'."
    }
    else {
        Write-Log "Using extract root as source root: '$sourceRoot'."
    }
}
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Write-Log "Created target directory '$TargetDir'."
}
Copy-Item -Path (Join-Path $sourceRoot '*') -Destination $TargetDir -Recurse -Force
Write-Log "Files copied from '$sourceRoot' to '$TargetDir'."
Start-Sleep -Milliseconds 300
$targetExePath = Join-Path $TargetDir $expectedExeName
if (-not (Test-Path $targetExePath)) {
    throw "Updated launcher executable was not found after copy: $targetExePath"
}
Write-Log "Launching updated executable '$targetExePath'."
Start-Process -FilePath $targetExePath -WorkingDirectory $TargetDir
Write-Log "Update apply finished successfully."
""";
    }
}
