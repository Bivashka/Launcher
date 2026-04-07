using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace BivLauncher.Client.Services;

public sealed class LauncherUpdateService(
    ISettingsService settingsService,
    ILogService logService) : ILauncherUpdateService
{
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

        using var response = await _httpClient.GetAsync(
            normalizedUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var extension = ResolvePackageExtension(normalizedUrl, response.Content.Headers);
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update package must be a .zip archive.");
        }

        var packagePath = Path.Combine(versionDirectory, $"launcher-update-{version}{extension}");
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[1024 * 128];
        long downloadedBytes = 0;
        var totalBytes = response.Content.Headers.ContentLength;
        while (true)
        {
            var read = await contentStream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;
            progress?.Report(new LauncherUpdateDownloadProgress(downloadedBytes, totalBytes));
        }

        await fileStream.FlushAsync(cancellationToken);
        logService.LogInfo($"Launcher update package downloaded: {packagePath}");
        return packagePath;
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
