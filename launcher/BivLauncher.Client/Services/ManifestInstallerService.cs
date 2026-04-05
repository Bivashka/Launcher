using BivLauncher.Client.Models;
using System.Security.Cryptography;
using System.IO.Compression;

namespace BivLauncher.Client.Services;

public sealed class ManifestInstallerService(
    ILauncherApiService launcherApiService,
    ILogService logService) : IManifestInstallerService
{
    private static readonly string[] ManagedCleanupRoots = ["mods/", "libraries/"];
    private const string AutoRuntimeRootRelativePath = ".bivlauncher/runtime";

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
        try
        {
            Directory.CreateDirectory(instanceDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(instanceDirectory, ex, "create the instance directory");
        }

        await EnsureRuntimeAsync(apiBaseUrl, manifest, instanceDirectory, cancellationToken);

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = file.Path.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.Combine(instanceDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            try
            {
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw CreateFileAccessException(destinationDirectory ?? destinationPath, ex, "create the destination directory");
            }

            var fileUpToDate = false;
            try
            {
                if (File.Exists(destinationPath))
                {
                    await using var existingStream = File.OpenRead(destinationPath);
                    var existingSha = await ComputeSha256Async(existingStream, cancellationToken);
                    fileUpToDate = existingSha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw CreateFileAccessException(destinationPath, ex, "read the existing file");
            }

            if (!fileUpToDate)
            {
                var tempPath = destinationPath + ".download";
                try
                {
                    if (File.Exists(tempPath))
                    {
                        EnsureWritableFile(tempPath);
                        File.Delete(tempPath);
                    }

                    var assetReference = string.IsNullOrWhiteSpace(file.DownloadUrl) ? file.S3Key : file.DownloadUrl;
                    await using (var sourceStream = await launcherApiService.OpenAssetReadStreamAsync(apiBaseUrl, assetReference, cancellationToken))
                    await using (var targetStream = File.Create(tempPath))
                    {
                        await sourceStream.CopyToAsync(targetStream, cancellationToken);
                    }

                    await using (var downloadedStream = File.OpenRead(tempPath))
                    {
                        var downloadedSha = await ComputeSha256Async(downloadedStream, cancellationToken);
                        if (!downloadedSha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            EnsureWritableFile(tempPath);
                            File.Delete(tempPath);
                            throw new InvalidOperationException($"Downloaded file hash mismatch for '{file.Path}'.");
                        }
                    }

                    EnsureWritableFile(destinationPath);
                    File.Move(tempPath, destinationPath, overwrite: true);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw CreateFileAccessException(destinationPath, ex, "write or replace the game file");
                }

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

        var removedFiles = CleanupOrphanFiles(instanceDirectory, manifest, cancellationToken);
        if (removedFiles > 0)
        {
            logService.LogInfo($"Removed orphan files: {removedFiles}");
        }

        return new InstallResult
        {
            InstanceDirectory = instanceDirectory,
            DownloadedFiles = downloaded,
            VerifiedFiles = verified,
            RemovedFiles = removedFiles
        };
    }

    private int CleanupOrphanFiles(string instanceDirectory, LauncherManifest manifest, CancellationToken cancellationToken)
    {
        var expectedPaths = new HashSet<string>(
            manifest.Files
                .Select(file => NormalizeRelativePath(file.Path))
                .Where(path => !string.IsNullOrWhiteSpace(path)),
            StringComparer.OrdinalIgnoreCase);

        var removedFiles = 0;
        foreach (var managedRoot in ManagedCleanupRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var managedRootPath = Path.Combine(
                instanceDirectory,
                managedRoot.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(managedRootPath))
            {
                continue;
            }

            var files = Directory.EnumerateFiles(managedRootPath, "*", SearchOption.AllDirectories).ToList();
            foreach (var absoluteFilePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedRelativePath = NormalizeRelativePath(Path.GetRelativePath(instanceDirectory, absoluteFilePath));
                if (string.IsNullOrWhiteSpace(normalizedRelativePath) ||
                    !normalizedRelativePath.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (expectedPaths.Contains(normalizedRelativePath))
                {
                    continue;
                }

                if (TryDeleteFile(absoluteFilePath))
                {
                    removedFiles++;
                    logService.LogInfo($"Removed orphan file: {normalizedRelativePath}");
                }
            }

            CleanupEmptyDirectories(managedRootPath, cancellationToken);
        }

        return removedFiles;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupEmptyDirectories(string rootPath, CancellationToken cancellationToken)
    {
        var directories = Directory
            .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (var directoryPath in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    Directory.Delete(directoryPath, recursive: false);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private async Task EnsureRuntimeAsync(
        string apiBaseUrl,
        LauncherManifest manifest,
        string instanceDirectory,
        CancellationToken cancellationToken)
    {
        var normalizedRuntimePath = NormalizeRelativePath(manifest.JavaRuntime ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(normalizedRuntimePath))
        {
            var runtimeAbsolutePath = Path.Combine(instanceDirectory, normalizedRuntimePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(runtimeAbsolutePath))
            {
                manifest.JavaRuntime = normalizedRuntimePath;
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.JavaRuntimeArtifactKey))
        {
            if (!string.IsNullOrWhiteSpace(normalizedRuntimePath))
            {
                logService.LogInfo("Manifest JavaRuntimeArtifactKey is missing; runtime auto-install skipped.");
            }
            return;
        }

        var runtimeArtifactKey = manifest.JavaRuntimeArtifactKey.Trim();
        var autoRuntimeRootRelativePath = BuildAutoRuntimeRootRelativePath(runtimeArtifactKey);
        var autoRuntimeRootAbsolutePath = Path.Combine(
            instanceDirectory,
            autoRuntimeRootRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(normalizedRuntimePath))
        {
            var detectedExistingRuntime = TryFindBundledJavaRelativePath(instanceDirectory, autoRuntimeRootAbsolutePath);
            if (!string.IsNullOrWhiteSpace(detectedExistingRuntime))
            {
                manifest.JavaRuntime = detectedExistingRuntime;
                logService.LogInfo($"Runtime auto-detected from cached artifact: {detectedExistingRuntime}");
                return;
            }
        }

        var artifactName = Path.GetFileName(runtimeArtifactKey);
        if (string.IsNullOrWhiteSpace(artifactName))
        {
            artifactName = $"runtime-{manifest.ProfileSlug}.bin";
        }

        var runtimeCacheDir = Path.Combine(instanceDirectory, ".runtime-cache");
        try
        {
            Directory.CreateDirectory(runtimeCacheDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(runtimeCacheDir, ex, "create the runtime cache directory");
        }
        var runtimeArtifactPath = Path.Combine(runtimeCacheDir, artifactName);
        try
        {
            if (File.Exists(runtimeArtifactPath))
            {
                EnsureWritableFile(runtimeArtifactPath);
                File.Delete(runtimeArtifactPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(runtimeArtifactPath, ex, "replace the cached runtime artifact");
        }

        var runtimeArtifactReference = string.IsNullOrWhiteSpace(manifest.JavaRuntimeArtifactUrl)
            ? runtimeArtifactKey
            : manifest.JavaRuntimeArtifactUrl;
        try
        {
            await using (var sourceStream = await launcherApiService.OpenAssetReadStreamAsync(apiBaseUrl, runtimeArtifactReference, cancellationToken))
            await using (var targetStream = File.Create(runtimeArtifactPath))
            {
                await sourceStream.CopyToAsync(targetStream, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(runtimeArtifactPath, ex, "write the runtime artifact");
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

        if (!string.IsNullOrWhiteSpace(normalizedRuntimePath))
        {
            var runtimeAbsolutePath = Path.Combine(instanceDirectory, normalizedRuntimePath.Replace('/', Path.DirectorySeparatorChar));
            if (runtimeArtifactPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ZipFile.ExtractToDirectory(runtimeArtifactPath, instanceDirectory, overwriteFiles: true);
                    EnsureWritableFile(runtimeArtifactPath);
                    File.Delete(runtimeArtifactPath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw CreateFileAccessException(instanceDirectory, ex, "extract the runtime archive");
                }
                logService.LogInfo($"Runtime archive extracted: {runtimeArtifactKey}");
            }
            else
            {
                var runtimeDir = Path.GetDirectoryName(runtimeAbsolutePath);
                try
                {
                    if (!string.IsNullOrWhiteSpace(runtimeDir))
                    {
                        Directory.CreateDirectory(runtimeDir);
                    }

                    EnsureWritableFile(runtimeAbsolutePath);
                    File.Move(runtimeArtifactPath, runtimeAbsolutePath, overwrite: true);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw CreateFileAccessException(runtimeAbsolutePath, ex, "install the runtime file");
                }
                logService.LogInfo($"Runtime binary installed: {runtimeArtifactKey}");
            }

            if (!File.Exists(runtimeAbsolutePath))
            {
                throw new InvalidOperationException($"Runtime file '{manifest.JavaRuntime}' was not found after runtime artifact install.");
            }

            manifest.JavaRuntime = normalizedRuntimePath;
            return;
        }

        PrepareAutoRuntimeDirectory(autoRuntimeRootAbsolutePath);
        if (runtimeArtifactPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ZipFile.ExtractToDirectory(runtimeArtifactPath, autoRuntimeRootAbsolutePath, overwriteFiles: true);
                EnsureWritableFile(runtimeArtifactPath);
                File.Delete(runtimeArtifactPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw CreateFileAccessException(autoRuntimeRootAbsolutePath, ex, "extract the auto-runtime archive");
            }
            logService.LogInfo($"Runtime archive extracted to auto-runtime root: {runtimeArtifactKey}");
        }
        else
        {
            var autoRuntimeBinDirectory = Path.Combine(autoRuntimeRootAbsolutePath, "bin");
            var runtimeAbsolutePath = Path.Combine(autoRuntimeBinDirectory, artifactName);
            try
            {
                Directory.CreateDirectory(autoRuntimeBinDirectory);
                EnsureWritableFile(runtimeAbsolutePath);
                File.Move(runtimeArtifactPath, runtimeAbsolutePath, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw CreateFileAccessException(runtimeAbsolutePath, ex, "install the auto-runtime file");
            }
            logService.LogInfo($"Runtime binary installed to auto-runtime root: {runtimeArtifactKey}");
        }

        var detectedRuntimePath = TryFindBundledJavaRelativePath(instanceDirectory, autoRuntimeRootAbsolutePath);
        if (string.IsNullOrWhiteSpace(detectedRuntimePath))
        {
            throw new InvalidOperationException(
                $"Runtime artifact '{runtimeArtifactKey}' was installed, but java executable was not found in '{autoRuntimeRootRelativePath}'.");
        }

        manifest.JavaRuntime = detectedRuntimePath;
        logService.LogInfo($"Runtime auto-detected from artifact: {detectedRuntimePath}");
    }

    private static void PrepareAutoRuntimeDirectory(string autoRuntimeRootAbsolutePath)
    {
        try
        {
            if (Directory.Exists(autoRuntimeRootAbsolutePath))
            {
                Directory.Delete(autoRuntimeRootAbsolutePath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup before runtime refresh.
        }

        try
        {
            Directory.CreateDirectory(autoRuntimeRootAbsolutePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(autoRuntimeRootAbsolutePath, ex, "create the auto-runtime directory");
        }
    }

    private static string BuildAutoRuntimeRootRelativePath(string runtimeArtifactKey)
    {
        var normalizedKey = NormalizeRelativePath(runtimeArtifactKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return AutoRuntimeRootRelativePath;
        }

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedKey));
        var shortHash = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        return $"{AutoRuntimeRootRelativePath}/{shortHash}";
    }

    private static string TryFindBundledJavaRelativePath(string instanceDirectory, string runtimeRootAbsolutePath)
    {
        if (!Directory.Exists(runtimeRootAbsolutePath))
        {
            return string.Empty;
        }

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "javaw.exe", "java.exe" }
            : new[] { "java" };
        var candidates = executableNames
            .SelectMany(executableName =>
                Directory.EnumerateFiles(runtimeRootAbsolutePath, executableName, SearchOption.AllDirectories))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        return NormalizeRelativePath(Path.GetRelativePath(instanceDirectory, candidates[0]));
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void EnsureWritableFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static InvalidOperationException CreateFileAccessException(string path, UnauthorizedAccessException ex, string operation)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "unknown path" : Path.GetFullPath(path);
        return new InvalidOperationException(
            $"Access denied while trying to {operation}: '{normalizedPath}'. " +
            "Close Minecraft/Java, disable file locks from antivirus/archivers, and make sure the install folder is writable.",
            ex);
    }
}
