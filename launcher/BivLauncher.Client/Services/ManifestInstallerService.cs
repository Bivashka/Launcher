using BivLauncher.Client.Models;
using System.Security.Cryptography;
using System.IO.Compression;

namespace BivLauncher.Client.Services;

public sealed class ManifestInstallerService(
    ILauncherApiService launcherApiService,
    ILogService logService) : IManifestInstallerService
{
    public async Task<InstallResult> VerifyAndInstallAsync(
        string apiBaseUrl,
        LauncherManifest manifest,
        string installDirectory,
        IProgress<InstallProgressInfo> progress,
        CancellationToken cancellationToken = default)
    {
        var totalFiles = manifest.Files.Count;
        var processed = 0;
        var downloaded = 0;
        var verified = 0;

        var instanceDirectory = Path.Combine(installDirectory, manifest.ProfileSlug);
        Directory.CreateDirectory(instanceDirectory);

        await EnsureRuntimeAsync(apiBaseUrl, manifest, instanceDirectory, cancellationToken);

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = file.Path.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.Combine(instanceDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var fileUpToDate = false;
            if (File.Exists(destinationPath))
            {
                await using var existingStream = File.OpenRead(destinationPath);
                var existingSha = await ComputeSha256Async(existingStream, cancellationToken);
                fileUpToDate = existingSha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
            }

            if (!fileUpToDate)
            {
                var tempPath = destinationPath + ".download";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                await using (var sourceStream = await launcherApiService.OpenAssetReadStreamAsync(apiBaseUrl, file.S3Key, cancellationToken))
                await using (var targetStream = File.Create(tempPath))
                {
                    await sourceStream.CopyToAsync(targetStream, cancellationToken);
                }

                await using (var downloadedStream = File.OpenRead(tempPath))
                {
                    var downloadedSha = await ComputeSha256Async(downloadedStream, cancellationToken);
                    if (!downloadedSha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tempPath);
                        throw new InvalidOperationException($"Downloaded file hash mismatch for '{file.Path}'.");
                    }
                }

                File.Move(tempPath, destinationPath, overwrite: true);
                downloaded++;
                logService.LogInfo($"Downloaded: {file.Path}");
            }
            else
            {
                verified++;
            }

            processed++;
            progress.Report(new InstallProgressInfo
            {
                Message = file.Path,
                ProcessedFiles = processed,
                TotalFiles = totalFiles,
                DownloadedFiles = downloaded,
                VerifiedFiles = verified,
                CurrentFilePath = file.Path
            });
        }

        return new InstallResult
        {
            InstanceDirectory = instanceDirectory,
            DownloadedFiles = downloaded,
            VerifiedFiles = verified
        };
    }

    private async Task EnsureRuntimeAsync(
        string apiBaseUrl,
        LauncherManifest manifest,
        string instanceDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.JavaRuntime))
        {
            return;
        }

        var runtimeRelativePath = manifest.JavaRuntime.Replace('/', Path.DirectorySeparatorChar);
        var runtimeAbsolutePath = Path.Combine(instanceDirectory, runtimeRelativePath);
        if (File.Exists(runtimeAbsolutePath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.JavaRuntimeArtifactKey))
        {
            logService.LogInfo("Manifest JavaRuntimeArtifactKey is missing; runtime auto-install skipped.");
            return;
        }

        var runtimeArtifactKey = manifest.JavaRuntimeArtifactKey.Trim();
        var artifactName = Path.GetFileName(runtimeArtifactKey);
        if (string.IsNullOrWhiteSpace(artifactName))
        {
            artifactName = $"runtime-{manifest.ProfileSlug}.bin";
        }

        var runtimeCacheDir = Path.Combine(instanceDirectory, ".runtime-cache");
        Directory.CreateDirectory(runtimeCacheDir);
        var runtimeArtifactPath = Path.Combine(runtimeCacheDir, artifactName);
        if (File.Exists(runtimeArtifactPath))
        {
            File.Delete(runtimeArtifactPath);
        }

        await using (var sourceStream = await launcherApiService.OpenAssetReadStreamAsync(apiBaseUrl, runtimeArtifactKey, cancellationToken))
        await using (var targetStream = File.Create(runtimeArtifactPath))
        {
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }

        if (manifest.JavaRuntimeArtifactSizeBytes is > 0 &&
            manifest.JavaRuntimeArtifactSizeBytes.Value != new FileInfo(runtimeArtifactPath).Length)
        {
            File.Delete(runtimeArtifactPath);
            throw new InvalidOperationException(
                $"Runtime artifact size mismatch for '{runtimeArtifactKey}'. " +
                $"Expected {manifest.JavaRuntimeArtifactSizeBytes.Value} bytes.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.JavaRuntimeArtifactSha256))
        {
            var expectedSha = manifest.JavaRuntimeArtifactSha256.Trim().ToLowerInvariant();
            await using var downloadedStream = File.OpenRead(runtimeArtifactPath);
            var downloadedSha = await ComputeSha256Async(downloadedStream, cancellationToken);
            if (!string.Equals(downloadedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(runtimeArtifactPath);
                throw new InvalidOperationException(
                    $"Runtime artifact hash mismatch for '{runtimeArtifactKey}'.");
            }

            logService.LogInfo($"Runtime artifact hash verified: {runtimeArtifactKey}");
        }

        if (runtimeArtifactPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(runtimeArtifactPath, instanceDirectory, overwriteFiles: true);
            File.Delete(runtimeArtifactPath);
            logService.LogInfo($"Runtime archive extracted: {runtimeArtifactKey}");
        }
        else
        {
            var runtimeDir = Path.GetDirectoryName(runtimeAbsolutePath);
            if (!string.IsNullOrWhiteSpace(runtimeDir))
            {
                Directory.CreateDirectory(runtimeDir);
            }

            File.Move(runtimeArtifactPath, runtimeAbsolutePath, overwrite: true);
            logService.LogInfo($"Runtime binary installed: {runtimeArtifactKey}");
        }

        if (!File.Exists(runtimeAbsolutePath))
        {
            throw new InvalidOperationException($"Runtime file '{manifest.JavaRuntime}' was not found after runtime artifact install.");
        }
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
