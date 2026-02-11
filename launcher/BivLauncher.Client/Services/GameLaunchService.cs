using BivLauncher.Client.Models;
using System.Diagnostics;
using System.Text;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

namespace BivLauncher.Client.Services;

public sealed class GameLaunchService(ILogService logService, ISettingsService settingsService) : IGameLaunchService
{
    private readonly ISettingsService _settingsService = settingsService;

    private static readonly string[] ImplicitMainClassCandidates =
    [
        "net.minecraft.client.main.Main",
        "net.minecraft.launchwrapper.Launch",
        "cpw.mods.modlauncher.Launcher",
        "cpw.mods.bootstraplauncher.BootstrapLauncher",
        "net.fabricmc.loader.impl.launch.knot.KnotClient",
        "org.quiltmc.loader.impl.launch.knot.KnotClient",
        "net.minecraft.client.Minecraft"
    ];

    private static readonly string[] LaunchArchiveExtensions = [".jar", ".jar2"];
    private static readonly string[] LaunchArchiveSearchPatterns = ["*.jar", "*.jar2"];

    public async Task<LaunchResult> LaunchAsync(
        LauncherManifest manifest,
        LauncherSettings settings,
        GameLaunchRoute route,
        string instanceDirectory,
        Action<string> onProcessLine,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(route.Address))
        {
            throw new InvalidOperationException("Launch route address is required.");
        }

        if (route.Port < 1 || route.Port > 65535)
        {
            throw new InvalidOperationException("Launch route port is out of range.");
        }

        var javaExecutable = ResolveJavaExecutable(settings.JavaMode, manifest.JavaRuntime, instanceDirectory);
        var jvmArgs = SplitArgs(manifest.JvmArgsDefault)
            .Where(x => !x.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) &&
                        !x.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
            .ToList();

        jvmArgs.Insert(0, $"-Xmx{settings.RamMb}M");
        jvmArgs.Insert(0, "-Xms1024M");

        var usePositionalLegacyArgs = ShouldUseLegacyPositionalArguments(route, manifest, instanceDirectory);
        var gameArgs = SplitArgs(manifest.GameArgsDefault).ToList();
        var forceAutoRouteArgs = StripControlFlag(gameArgs, "--bl-force-route");
        var disableAutoRouteArgs = StripControlFlag(gameArgs, "--bl-no-route");

        // Pre-1.6 launchwrapper clients often have their own in-game server selector in minecraft.jar.
        // Keep launcher route selection available, but require explicit opt-in via --bl-force-route.
        if (usePositionalLegacyArgs && !disableAutoRouteArgs && !forceAutoRouteArgs)
        {
            disableAutoRouteArgs = true;
            logService.LogInfo("Auto route args are disabled by default for pre-1.6 compatibility. Add --bl-force-route to enable launcher auto-connect.");
        }

        if (!disableAutoRouteArgs)
        {
            AppendRouteArgs(gameArgs, route.Address, route.Port);
        }
        else
        {
            logService.LogInfo("Auto route args are disabled by --bl-no-route manifest flag.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = javaExecutable,
            WorkingDirectory = instanceDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in jvmArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var launchMode = NormalizeLaunchMode(manifest.LaunchMode);
        var resolvedMainClassForCompatibility = string.Empty;
        var resolvedClasspathForCompatibility = string.Empty;
        var legacyCompatibilityHome = string.Empty;
        if (launchMode == "mainclass")
        {
            var launchMainClass = manifest.LaunchMainClass.Trim();
            if (string.IsNullOrWhiteSpace(launchMainClass))
            {
                throw new InvalidOperationException("Manifest launchMainClass is required for mainclass mode.");
            }

            var classpathEntries = ResolveClasspathEntries(manifest, instanceDirectory, route.PreferredJarPath);
            if (!TryResolveMainClassFromClasspath(
                    classpathEntries,
                    launchMainClass,
                    out var resolvedMainClass,
                    out var mainClassResolveReason))
            {
                throw new InvalidOperationException(
                    $"Manifest launchMainClass '{launchMainClass}' is not available in launch classpath. {mainClassResolveReason}");
            }

            if (!string.Equals(resolvedMainClass, launchMainClass, StringComparison.OrdinalIgnoreCase))
            {
                logService.LogInfo(
                    $"Configured main class '{launchMainClass}' was not found in classpath. Falling back to '{resolvedMainClass}'.");
            }

            var classpath = string.Join(Path.PathSeparator, classpathEntries);
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(classpath);
            startInfo.ArgumentList.Add(resolvedMainClass);
            resolvedMainClassForCompatibility = resolvedMainClass;
            resolvedClasspathForCompatibility = classpath;
        }
        else
        {
            var gameJar = ResolveGameJar(manifest, instanceDirectory, route.PreferredJarPath);
            if (TryValidateJarArchive(gameJar, requireMainClass: true, out _))
            {
                startInfo.ArgumentList.Add("-jar");
                startInfo.ArgumentList.Add(gameJar);
            }
            else if (TryResolveImplicitMainClassLaunch(manifest, instanceDirectory, gameJar, out var implicitMainClass, out var implicitClasspath, out var implicitReason))
            {
                var jarRelativePath = NormalizePath(Path.GetRelativePath(instanceDirectory, gameJar));
                logService.LogInfo(
                    $"Jar '{jarRelativePath}' has no Main-Class. Using implicit mainclass launch: {implicitMainClass}.");
                startInfo.ArgumentList.Add("-cp");
                startInfo.ArgumentList.Add(implicitClasspath);
                startInfo.ArgumentList.Add(implicitMainClass);
                resolvedMainClassForCompatibility = implicitMainClass;
                resolvedClasspathForCompatibility = implicitClasspath;
            }
            else
            {
                var jarRelativePath = NormalizePath(Path.GetRelativePath(instanceDirectory, gameJar));
                throw new InvalidDataException(
                    $"Selected route jar '{jarRelativePath}' is not launchable with -jar and implicit mainclass fallback failed. {implicitReason}");
            }
        }

        if (RequiresInstanceHomeCompatibility(resolvedMainClassForCompatibility))
        {
            EnsureArgumentWithValue(gameArgs, "--gameDir", instanceDirectory);
            EnsureArgumentWithValue(gameArgs, "--assetsDir", Path.Combine(instanceDirectory, "assets"));
            EnsureLegacyJvmNativePaths(startInfo.ArgumentList, instanceDirectory);
            if (string.Equals(resolvedMainClassForCompatibility, "net.minecraft.launchwrapper.Launch", StringComparison.OrdinalIgnoreCase))
            {
                if (usePositionalLegacyArgs)
                {
                    logService.LogInfo("Legacy launchwrapper compatibility: using positional auth/route args for pre-1.6 client.");
                }

                EnsureLegacyAuthArguments(gameArgs, settings, usePositionalLegacyArgs);
                EnsureLegacyRouteArguments(gameArgs, route, disableAutoRouteArgs, usePositionalLegacyArgs);
                EnsureLegacyLaunchwrapperDefaults(gameArgs, route, manifest, instanceDirectory, resolvedClasspathForCompatibility);
            }
            legacyCompatibilityHome = PrepareLegacyCompatibilityHome(instanceDirectory);
            // Some legacy launchers still resolve minecraft home via APPDATA/HOME.
            startInfo.Environment["APPDATA"] = legacyCompatibilityHome;
            startInfo.Environment["HOME"] = legacyCompatibilityHome;
            startInfo.Environment["USERPROFILE"] = legacyCompatibilityHome;
            logService.LogInfo(
                $"Legacy compatibility mode enabled: gameDir={instanceDirectory}, compatHome={legacyCompatibilityHome}.");
        }

        foreach (var arg in gameArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        logService.LogInfo($"Process started: {startInfo.FileName} {BuildArgsPreview(startInfo.ArgumentList)}");
        logService.LogInfo($"Process id: {process.Id}");

        using var cancellationRegistration = cancellationToken.Register(() =>
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
            }
        });

        var stdOutTask = PumpOutputAsync(process.StandardOutput, onProcessLine, cancellationToken);
        var stdErrTask = PumpOutputAsync(process.StandardError, onProcessLine, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var pumpTask = Task.WhenAll(stdOutTask, stdErrTask);
        var drainCompleted = await Task.WhenAny(
            pumpTask,
            Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
        if (drainCompleted == pumpTask)
        {
            await pumpTask;
        }
        else
        {
            logService.LogInfo("Process exited, but output streams stayed open. Continuing without waiting for stream close.");
        }

        logService.LogInfo($"Process exited with code {process.ExitCode}");

        return new LaunchResult
        {
            ExitCode = process.ExitCode,
            JavaExecutable = javaExecutable
        };
    }

    private static async Task PumpOutputAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            pending.Append(buffer, 0, read);
            FlushCompletedLines(pending, onLine);
        }

        if (pending.Length > 0)
        {
            var tail = pending.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                onLine(tail);
            }
        }
    }

    private static void FlushCompletedLines(StringBuilder pending, Action<string> onLine)
    {
        var start = 0;
        for (var i = 0; i < pending.Length; i++)
        {
            if (pending[i] != '\n')
            {
                continue;
            }

            var length = i - start;
            if (length > 0 && pending[i - 1] == '\r')
            {
                length--;
            }

            if (length > 0)
            {
                var line = pending.ToString(start, length).Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    onLine(line);
                }
            }

            start = i + 1;
        }

        if (start > 0)
        {
            pending.Remove(0, start);
        }
    }

    private static string ResolveJavaExecutable(string javaMode, string? javaRuntime, string instanceDirectory)
    {
        if (javaMode.Equals("Bundled", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(javaRuntime))
            {
                throw new InvalidOperationException("Bundled Java mode selected but manifest has no JavaRuntime.");
            }

            var runtimePath = Path.Combine(instanceDirectory, javaRuntime.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(runtimePath))
            {
                return runtimePath;
            }

            throw new FileNotFoundException("Bundled Java runtime executable is missing.", runtimePath);
        }

        return "java";
    }

    private string ResolveGameJar(LauncherManifest manifest, string instanceDirectory, string preferredJarPath)
    {
        var normalizedPreferred = NormalizeRelativeJarPath(preferredJarPath);
        if (!string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            var preferredAbsolutePath = ResolveSafePath(instanceDirectory, normalizedPreferred);
            if (File.Exists(preferredAbsolutePath))
            {
                if (TryValidateJarArchive(preferredAbsolutePath, requireMainClass: false, out _))
                {
                    return preferredAbsolutePath;
                }

                logService.LogInfo(
                    $"Selected route jar '{normalizedPreferred}' is not a valid JAR archive. Trying fallback jar.");

                var fallbackByPreferred = ResolveBestJarCandidate(
                    manifest,
                    instanceDirectory,
                    preferredFileName: Path.GetFileName(normalizedPreferred),
                    excludedAbsolutePath: preferredAbsolutePath,
                    requireMainClass: false,
                    out var fallbackReason);
                if (!string.IsNullOrWhiteSpace(fallbackByPreferred))
                {
                    var resolvedRelative = NormalizePath(Path.GetRelativePath(instanceDirectory, fallbackByPreferred));
                    logService.LogInfo($"Fallback jar selected: {resolvedRelative}");
                    return fallbackByPreferred;
                }

                throw new InvalidDataException(
                    $"Selected route jar '{normalizedPreferred}' is not a valid JAR archive. " +
                    $"No fallback jar found. Details: {fallbackReason}");
            }

            var preferredFileName = Path.GetFileName(normalizedPreferred);
            var fallbackByMissingPreferred = ResolveBestJarCandidate(
                manifest,
                instanceDirectory,
                preferredFileName,
                excludedAbsolutePath: string.Empty,
                requireMainClass: false,
                out var fallbackMissingReason);
            if (!string.IsNullOrWhiteSpace(fallbackByMissingPreferred))
            {
                var resolvedRelative = NormalizePath(Path.GetRelativePath(instanceDirectory, fallbackByMissingPreferred));
                logService.LogInfo(
                    $"Selected route jar '{normalizedPreferred}' was not found. Fallback jar: {resolvedRelative}");
                return fallbackByMissingPreferred;
            }

            throw new FileNotFoundException(
                $"Selected route jar '{normalizedPreferred}' does not exist and no fallback launchable jar was found. " +
                $"Details: {fallbackMissingReason}",
                preferredAbsolutePath);
        }

        var autoResolved = ResolveBestJarCandidate(
            manifest,
            instanceDirectory,
            preferredFileName: string.Empty,
            excludedAbsolutePath: string.Empty,
            requireMainClass: false,
            out var autoResolveReason);
        if (!string.IsNullOrWhiteSpace(autoResolved))
        {
            return autoResolved;
        }

        throw new InvalidOperationException(
            $"No launchable .jar/.jar2 file found in manifest or instance directory. Details: {autoResolveReason}");
    }

    private static bool TryResolveImplicitMainClassLaunch(
        LauncherManifest manifest,
        string instanceDirectory,
        string gameJarPath,
        out string mainClass,
        out string classpath,
        out string reason)
    {
        var classpathEntries = BuildImplicitClasspathEntries(instanceDirectory, gameJarPath);
        if (classpathEntries.Count == 0)
        {
            mainClass = string.Empty;
            classpath = string.Empty;
            reason = "No classpath jars found for implicit launch.";
            return false;
        }

        var explicitMainClass = manifest.LaunchMainClass?.Trim() ?? string.Empty;
        if (TryResolveMainClassFromClasspath(classpathEntries, explicitMainClass, out mainClass, out reason))
        {
            classpath = string.Join(Path.PathSeparator, classpathEntries);
            return true;
        }

        mainClass = string.Empty;
        classpath = string.Empty;
        return false;
    }

    private static bool TryResolveMainClassFromClasspath(
        IReadOnlyList<string> classpathEntries,
        string preferredMainClass,
        out string resolvedMainClass,
        out string reason)
    {
        var candidates = new List<string>();
        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            if (seenCandidates.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }

        AddCandidate(preferredMainClass?.Trim() ?? string.Empty);
        foreach (var candidate in ImplicitMainClassCandidates)
        {
            AddCandidate(candidate);
        }

        foreach (var candidate in candidates)
        {
            if (ContainsClass(classpathEntries, candidate))
            {
                resolvedMainClass = candidate;
                reason = string.Empty;
                return true;
            }
        }

        resolvedMainClass = string.Empty;
        reason = $"No known main class found in classpath jars. Checked: {string.Join(", ", candidates)}.";
        return false;
    }

    private static List<string> BuildImplicitClasspathEntries(string instanceDirectory, string gameJarPath)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return;
            }

            if (seen.Add(fullPath))
            {
                entries.Add(fullPath);
            }
        }

        AddIfExists(gameJarPath);

        var librariesPath = Path.Combine(instanceDirectory, "libraries");
        if (Directory.Exists(librariesPath))
        {
            foreach (var jar in EnumerateLaunchArchives(librariesPath, SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                AddIfExists(jar);
            }
        }

        foreach (var jar in EnumerateLaunchArchives(instanceDirectory, SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            AddIfExists(jar);
        }

        return entries;
    }

    private static bool ContainsClass(IReadOnlyList<string> classpathJars, string className)
    {
        var entryName = className.Replace('.', '/') + ".class";
        foreach (var jarPath in classpathJars)
        {
            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                if (archive.Entries.Any(x => x.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore unreadable jars while probing classpath candidates.
            }
        }

        return false;
    }

    private static List<string> ResolveClasspathEntries(LauncherManifest manifest, string instanceDirectory, string preferredJarPath)
    {
        var resolved = new List<string>();

        var classpathEntries = manifest.LaunchClasspath ?? [];
        foreach (var entry in classpathEntries)
        {
            var matches = ExpandClasspathEntry(instanceDirectory, entry);
            foreach (var match in matches)
            {
                if (!resolved.Contains(match, StringComparer.OrdinalIgnoreCase))
                {
                    resolved.Add(match);
                }
            }
        }

        var selectedRouteJar = TryResolveRouteJar(manifest, instanceDirectory, preferredJarPath);
        if (!string.IsNullOrWhiteSpace(selectedRouteJar) &&
            !resolved.Contains(selectedRouteJar, StringComparer.OrdinalIgnoreCase))
        {
            resolved.Add(selectedRouteJar);
        }

        if (resolved.Count == 0)
        {
            throw new InvalidOperationException("Launch classpath is empty.");
        }

        return resolved;
    }

    private static IReadOnlyList<string> ExpandClasspathEntry(string instanceDirectory, string rawEntry)
    {
        var entry = rawEntry.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(entry))
        {
            return [];
        }

        if (entry.Contains("**", StringComparison.Ordinal))
        {
            return ExpandRecursiveGlob(instanceDirectory, entry);
        }

        if (entry.Contains('*') || entry.Contains('?'))
        {
            var directoryPart = Path.GetDirectoryName(entry)?.Replace('\\', '/') ?? string.Empty;
            var pattern = Path.GetFileName(entry);
            var directoryPath = ResolveSafePath(instanceDirectory, directoryPart);
            if (!Directory.Exists(directoryPath))
            {
                return [];
            }

            return Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var directPath = ResolveSafePath(instanceDirectory, entry);
        if (!File.Exists(directPath))
        {
            return [];
        }

        return [directPath];
    }

    private static IReadOnlyList<string> ExpandRecursiveGlob(string instanceDirectory, string entry)
    {
        var marker = "/**/";
        var markerIndex = entry.IndexOf(marker, StringComparison.Ordinal);
        string basePath;
        string pattern;

        if (markerIndex >= 0)
        {
            basePath = entry[..markerIndex];
            pattern = entry[(markerIndex + marker.Length)..];
        }
        else if (entry.EndsWith("/**", StringComparison.Ordinal))
        {
            basePath = entry[..^3];
            pattern = "*";
        }
        else
        {
            throw new InvalidOperationException($"Unsupported recursive classpath entry '{entry}'.");
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "*";
        }

        var rootPath = ResolveSafePath(instanceDirectory, basePath);
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        return Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveSafePath(string instanceDirectory, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(instanceDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootPath = Path.GetFullPath(instanceDirectory);
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Classpath entry '{relativePath}' escapes instance directory.");
        }

        return fullPath;
    }

    private static string NormalizePath(string rawPath)
    {
        return rawPath.Replace('\\', '/');
    }

    private static bool IsLaunchArchivePath(string path)
    {
        var extension = Path.GetExtension(path);
        return LaunchArchiveExtensions.Any(candidate =>
            string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateLaunchArchives(string directoryPath, SearchOption searchOption)
    {
        foreach (var pattern in LaunchArchiveSearchPatterns)
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, pattern, searchOption))
            {
                yield return filePath;
            }
        }
    }

    private static string TryResolveRouteJar(LauncherManifest manifest, string instanceDirectory, string preferredJarPath)
    {
        var normalized = NormalizeRelativeJarPath(preferredJarPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var path = ResolveSafePath(instanceDirectory, normalized);
        if (File.Exists(path) && TryValidateJarArchive(path, requireMainClass: false, out _))
        {
            return path;
        }

        var preferredFileName = Path.GetFileName(normalized);
        return ResolveBestJarCandidate(
            manifest,
            instanceDirectory,
            preferredFileName,
            excludedAbsolutePath: path,
            requireMainClass: false,
            out _);
    }

    private static string ResolveBestJarCandidate(
        LauncherManifest manifest,
        string instanceDirectory,
        string preferredFileName,
        string excludedAbsolutePath,
        bool requireMainClass,
        out string diagnostic)
    {
        var normalizedPreferredFileName = string.IsNullOrWhiteSpace(preferredFileName)
            ? string.Empty
            : preferredFileName.Trim();
        var normalizedExcludedAbsolutePath = string.IsNullOrWhiteSpace(excludedAbsolutePath)
            ? string.Empty
            : Path.GetFullPath(excludedAbsolutePath);

        var candidates = new List<(string RelativePath, string AbsolutePath, int Score)>();
        var seenAbsolutePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifestPath in manifest.Files.Select(x => x.Path))
        {
            if (!IsLaunchArchivePath(manifestPath))
            {
                continue;
            }

            var absolutePath = ResolveSafePath(instanceDirectory, manifestPath);
            if (!File.Exists(absolutePath))
            {
                continue;
            }

            if (!seenAbsolutePaths.Add(absolutePath))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedExcludedAbsolutePath) &&
                string.Equals(Path.GetFullPath(absolutePath), normalizedExcludedAbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedRelativePath = NormalizePath(manifestPath);
            candidates.Add((normalizedRelativePath, absolutePath, ScoreJarCandidate(normalizedRelativePath, normalizedPreferredFileName, fromManifest: true)));
        }

        if (Directory.Exists(instanceDirectory))
        {
            foreach (var absolutePath in EnumerateLaunchArchives(instanceDirectory, SearchOption.AllDirectories))
            {
                if (!seenAbsolutePaths.Add(absolutePath))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(normalizedExcludedAbsolutePath) &&
                    string.Equals(Path.GetFullPath(absolutePath), normalizedExcludedAbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedRelativePath = NormalizePath(Path.GetRelativePath(instanceDirectory, absolutePath));
                candidates.Add((normalizedRelativePath, absolutePath, ScoreJarCandidate(normalizedRelativePath, normalizedPreferredFileName, fromManifest: false)));
            }
        }

        var invalidDiagnostics = new List<string>();
        foreach (var candidate in candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.RelativePath.Length)
            .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsJarEligibleForJarMode(candidate.RelativePath, normalizedPreferredFileName, candidate.AbsolutePath))
            {
                continue;
            }

            if (TryValidateJarArchive(candidate.AbsolutePath, requireMainClass, out var validationReason))
            {
                diagnostic = string.Empty;
                return candidate.AbsolutePath;
            }

            if (invalidDiagnostics.Count < 3)
            {
                invalidDiagnostics.Add($"{candidate.RelativePath}: {validationReason}");
            }
        }

        diagnostic = invalidDiagnostics.Count == 0
            ? "No .jar/.jar2 candidates found."
            : string.Join(" | ", invalidDiagnostics);
        return string.Empty;
    }

    private static bool IsJarEligibleForJarMode(string relativePath, string preferredFileName, string absolutePath)
    {
        var normalizedRelativePath = NormalizePath(relativePath).TrimStart('/');
        var fileName = Path.GetFileName(normalizedRelativePath);
        var isPreferredNameMatch = !string.IsNullOrWhiteSpace(preferredFileName) &&
                                   string.Equals(fileName, preferredFileName, StringComparison.OrdinalIgnoreCase);
        if (isPreferredNameMatch)
        {
            return true;
        }

        if (IsInfrastructureJarPath(normalizedRelativePath))
        {
            return false;
        }

        if (LooksLikeGameJarName(fileName))
        {
            return true;
        }

        try
        {
            var fileInfo = new FileInfo(absolutePath);
            return !normalizedRelativePath.Contains('/', StringComparison.Ordinal) &&
                   fileInfo.Exists &&
                   fileInfo.Length >= 5L * 1024L * 1024L;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInfrastructureJarPath(string normalizedRelativePath)
    {
        return normalizedRelativePath.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.Contains("/libraries/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.Contains("/lib/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.Contains("/mods/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.StartsWith("natives/", StringComparison.OrdinalIgnoreCase) ||
               normalizedRelativePath.Contains("/natives/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeGameJarName(string fileName)
    {
        return fileName.Contains("minecraft", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("client", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("game", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("launcher", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreJarCandidate(string relativePath, string preferredFileName, bool fromManifest)
    {
        var normalizedRelativePath = NormalizePath(relativePath).TrimStart('/');
        var fileName = Path.GetFileName(normalizedRelativePath);
        var score = fromManifest ? 250 : 0;

        if (!string.IsNullOrWhiteSpace(preferredFileName) &&
            string.Equals(fileName, preferredFileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (string.Equals(fileName, "minecraft.jar", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "minecraft.jar2", StringComparison.OrdinalIgnoreCase))
        {
            score += 900;
        }
        else if (fileName.Contains("minecraft", StringComparison.OrdinalIgnoreCase))
        {
            score += 650;
        }

        if (!normalizedRelativePath.Contains('/', StringComparison.Ordinal))
        {
            score += 280;
        }

        if (normalizedRelativePath.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelativePath.Contains("/libraries/", StringComparison.OrdinalIgnoreCase))
        {
            score -= 700;
        }

        if (normalizedRelativePath.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelativePath.Contains("/lib/", StringComparison.OrdinalIgnoreCase))
        {
            score -= 250;
        }

        if (normalizedRelativePath.StartsWith("natives/", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelativePath.Contains("/natives/", StringComparison.OrdinalIgnoreCase))
        {
            score -= 320;
        }

        return score;
    }

    private static bool TryValidateJarArchive(string filePath, bool requireMainClass, out string reason)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                reason = "file not found";
                return false;
            }

            if (fileInfo.Length < 4)
            {
                reason = "file is too small";
                return false;
            }

            using var archive = ZipFile.OpenRead(filePath);
            if (archive.Entries.Count == 0)
            {
                reason = "archive has no entries";
                return false;
            }

            if (!requireMainClass)
            {
                reason = string.Empty;
                return true;
            }

            var manifestEntry = archive.Entries
                .FirstOrDefault(x => x.FullName.Equals("META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null)
            {
                reason = "META-INF/MANIFEST.MF is missing";
                return false;
            }

            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var manifestContent = reader.ReadToEnd();
            var hasMainClass = manifestContent
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.TrimStart().StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase));
            if (!hasMainClass)
            {
                reason = "Main-Class is missing in MANIFEST.MF";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static string NormalizeRelativeJarPath(string rawPath)
    {
        return string.IsNullOrWhiteSpace(rawPath)
            ? string.Empty
            : rawPath.Trim().Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeLaunchMode(string mode)
    {
        if (string.Equals(mode, "mainclass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "main-class", StringComparison.OrdinalIgnoreCase))
        {
            return "mainclass";
        }

        return "jar";
    }

    private static IEnumerable<string> SplitArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return [];
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in args)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static bool StripControlFlag(List<string> args, string flag)
    {
        var removedAny = false;
        var index = args.FindIndex(x => x.Equals(flag, StringComparison.OrdinalIgnoreCase));
        while (index >= 0)
        {
            args.RemoveAt(index);
            removedAny = true;
            index = args.FindIndex(x => x.Equals(flag, StringComparison.OrdinalIgnoreCase));
        }

        return removedAny;
    }

    private static string BuildArgsPreview(IEnumerable<string> args)
    {
        return string.Join(' ', args.Select(QuoteIfNeeded));
    }

    private static bool RequiresInstanceHomeCompatibility(string mainClass)
    {
        if (string.IsNullOrWhiteSpace(mainClass))
        {
            return false;
        }

        return string.Equals(mainClass, "net.minecraft.launchwrapper.Launch", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mainClass, "net.minecraft.client.main.Main", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mainClass, "net.minecraft.client.Minecraft", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureArgumentWithValue(List<string> args, string key, string value)
    {
        RemoveArgWithValue(args, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        args.Add(key);
        args.Add(value);
    }

    private void EnsureLegacyJvmNativePaths(IList<string> jvmArgs, string instanceDirectory)
    {
        var nativesDirectory = Path.Combine(instanceDirectory, "natives");
        if (!Directory.Exists(nativesDirectory))
        {
            return;
        }

        var insertionIndex = FindLaunchModeArgumentIndex(jvmArgs);
        insertionIndex = EnsureJvmProperty(jvmArgs, "java.library.path", nativesDirectory, insertionIndex);
        insertionIndex = EnsureJvmProperty(jvmArgs, "org.lwjgl.librarypath", nativesDirectory, insertionIndex);
        _ = EnsureJvmProperty(jvmArgs, "net.java.games.input.librarypath", nativesDirectory, insertionIndex);
        logService.LogInfo($"Legacy JVM native paths configured: {nativesDirectory}");
    }

    private static int EnsureJvmProperty(IList<string> args, string propertyName, string value, int insertionIndex)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return insertionIndex;
        }

        var prefix = $"-D{propertyName}=";
        for (var i = args.Count - 1; i >= 0; i--)
        {
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (i < insertionIndex)
                {
                    insertionIndex--;
                }

                args.RemoveAt(i);
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return insertionIndex;
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, args.Count);
        args.Insert(insertionIndex, $"{prefix}{value}");
        return insertionIndex + 1;
    }

    private static int FindLaunchModeArgumentIndex(IList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals("-cp", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-classpath", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-jar", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return args.Count;
    }

    private void EnsureLegacyLaunchwrapperDefaults(
        List<string> gameArgs,
        GameLaunchRoute route,
        LauncherManifest manifest,
        string instanceDirectory,
        string resolvedClasspath)
    {
        var mcVersionArg = ResolveLegacyVersion(route, manifest, instanceDirectory);
        EnsureArgumentWithValue(gameArgs, "--version", mcVersionArg);
        logService.LogInfo($"Legacy launchwrapper args: using --version {mcVersionArg}.");

        var hasForgeTweaker =
            ContainsClasspathClass(resolvedClasspath, "cpw.mods.fml.common.launcher.FMLTweaker") ||
            Directory.Exists(Path.Combine(instanceDirectory, "libraries", "forge")) ||
            Directory.Exists(Path.Combine(instanceDirectory, "coremods"));

        if (!hasForgeTweaker)
        {
            RemoveArgWithValue(gameArgs, "--tweakClass");
            return;
        }

        EnsureArgumentWithValue(gameArgs, "--tweakClass", "cpw.mods.fml.common.launcher.FMLTweaker");
        logService.LogInfo("Legacy launchwrapper args: using --tweakClass cpw.mods.fml.common.launcher.FMLTweaker.");
    }

    private void EnsureLegacyAuthArguments(List<string> gameArgs, LauncherSettings settings, bool usePositionalLegacyArgs)
    {
        var rawUsername = string.IsNullOrWhiteSpace(settings.PlayerAuthUsername)
            ? settings.LastPlayerUsername.Trim()
            : settings.PlayerAuthUsername.Trim();
        var username = NormalizeLegacyUsername(rawUsername);

        var sessionToken = settings.PlayerAuthToken.Trim();
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            sessionToken = "0";
        }

        if (usePositionalLegacyArgs)
        {
            RemoveArgWithValue(gameArgs, "--username");
            RemoveArgWithValue(gameArgs, "--session");
            RemoveArgWithValue(gameArgs, "--uuid");

            // Pre-1.6 clients expect positional args: username, session, server, port.
            gameArgs.Insert(0, sessionToken);
            gameArgs.Insert(0, username);

            logService.LogInfo(
                $"Legacy auth args prepared (positional): username={username}, sourceUsername={rawUsername}, sessionTokenLength={sessionToken.Length}.");
            return;
        }

        EnsureArgumentWithValue(gameArgs, "--username", username);
        EnsureArgumentWithValue(gameArgs, "--session", sessionToken);

        var externalId = settings.PlayerAuthExternalId.Trim();
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            EnsureArgumentWithValue(gameArgs, "--uuid", externalId);
        }

        logService.LogInfo(
            $"Legacy auth args prepared: username={username}, sourceUsername={rawUsername}, sessionTokenLength={sessionToken.Length}, hasUuid={!string.IsNullOrWhiteSpace(externalId)}.");
    }

    private void EnsureLegacyRouteArguments(
        List<string> gameArgs,
        GameLaunchRoute route,
        bool routeArgsExplicitlyDisabled,
        bool usePositionalLegacyArgs)
    {
        if (usePositionalLegacyArgs)
        {
            RemoveArgWithValue(gameArgs, "--server");
            RemoveArgWithValue(gameArgs, "--port");

            var positionalServer = route.Address.Trim();
            var positionalPort = route.Port.ToString(CultureInfo.InvariantCulture);
            if (routeArgsExplicitlyDisabled)
            {
                logService.LogInfo(
                    "Legacy launchwrapper args: route auto-connect disabled; passing empty positional server/port for in-game server selection.");
                positionalServer = string.Empty;
                positionalPort = "0";
            }

            // Keep positional route right after positional auth.
            gameArgs.Insert(Math.Min(2, gameArgs.Count), positionalServer);
            gameArgs.Insert(Math.Min(3, gameArgs.Count), positionalPort);
            return;
        }

        var hasServer = TryGetArgumentValue(gameArgs, "--server", out var currentServer);
        var hasPort = TryGetArgumentValue(gameArgs, "--port", out var currentPort);
        if (hasServer && hasPort && !string.IsNullOrWhiteSpace(currentServer) && !string.IsNullOrWhiteSpace(currentPort))
        {
            return;
        }

        if (routeArgsExplicitlyDisabled)
        {
            logService.LogInfo(
                "Legacy launchwrapper args: --bl-no-route requested but route args were missing. Injecting --server/--port for compatibility.");
        }

        EnsureArgumentWithValue(gameArgs, "--server", route.Address.Trim());
        EnsureArgumentWithValue(gameArgs, "--port", route.Port.ToString(CultureInfo.InvariantCulture));
    }

    private static bool ShouldUseLegacyPositionalArguments(
        GameLaunchRoute route,
        LauncherManifest manifest,
        string instanceDirectory)
    {
        var resolvedVersion = ResolveLegacyVersion(route, manifest, instanceDirectory);
        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            return false;
        }

        if (string.Equals(resolvedVersion, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryParseMajorMinorVersion(resolvedVersion, out var major, out var minor))
        {
            return false;
        }

        return major == 1 && minor <= 5;
    }

    private static bool TryParseMajorMinorVersion(string rawVersion, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var normalized = rawVersion.Trim();
        var firstDot = normalized.IndexOf('.');
        if (firstDot <= 0 || firstDot + 1 >= normalized.Length)
        {
            return false;
        }

        var secondDot = normalized.IndexOf('.', firstDot + 1);
        var majorPart = normalized[..firstDot];
        var minorPart = secondDot > firstDot
            ? normalized.Substring(firstDot + 1, secondDot - firstDot - 1)
            : normalized[(firstDot + 1)..];

        majorPart = ExtractLeadingDigits(majorPart);
        minorPart = ExtractLeadingDigits(minorPart);
        if (string.IsNullOrWhiteSpace(majorPart) || string.IsNullOrWhiteSpace(minorPart))
        {
            return false;
        }

        return int.TryParse(majorPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out major) &&
               int.TryParse(minorPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
    }

    private static string ExtractLeadingDigits(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var index = 0;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            index++;
        }

        return index == 0 ? string.Empty : value[..index];
    }

    private static string ResolveLegacyVersion(GameLaunchRoute route, LauncherManifest manifest, string instanceDirectory)
    {
        var routeVersion = NormalizeVersion(route.McVersion);
        if (!string.IsNullOrWhiteSpace(routeVersion))
        {
            return routeVersion;
        }

        var manifestVersion = NormalizeVersion(manifest.McVersion);
        if (!string.IsNullOrWhiteSpace(manifestVersion))
        {
            return manifestVersion;
        }

        var inferred = TryInferVersionFromDeobfMap(instanceDirectory);
        if (!string.IsNullOrWhiteSpace(inferred))
        {
            return inferred;
        }

        return "legacy";
    }

    private static string NormalizeVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Trim();
        return string.Equals(normalized, "1.21.1", StringComparison.OrdinalIgnoreCase) ? string.Empty : normalized;
    }

    private static string TryInferVersionFromDeobfMap(string instanceDirectory)
    {
        const string prefix = "deobfuscation_data_";
        const string suffix = ".zip";

        foreach (var filePath in Directory.EnumerateFiles(instanceDirectory, "deobfuscation_data_*.zip", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(filePath);
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                fileName.Length <= prefix.Length + suffix.Length)
            {
                continue;
            }

            var version = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length).Trim();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }

        return string.Empty;
    }

    private static bool ContainsClasspathClass(string classpath, string className)
    {
        if (string.IsNullOrWhiteSpace(classpath))
        {
            return false;
        }

        var entries = classpath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .ToList();
        if (entries.Count == 0)
        {
            return false;
        }

        return ContainsClass(entries, className);
    }

    private static bool HasArgument(IEnumerable<string> args, string key)
    {
        var prefix = key + "=";
        return args.Any(x =>
            x.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetArgumentValue(IReadOnlyList<string> args, string key, out string value)
    {
        var prefix = key + "=";

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var inlineValue = args[i][prefix.Length..];
                if (!string.IsNullOrWhiteSpace(inlineValue))
                {
                    value = inlineValue;
                    return true;
                }

                break;
            }

            if (!args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Count)
            {
                break;
            }

            var next = args[i + 1];
            if (next.StartsWith("-", StringComparison.Ordinal))
            {
                break;
            }

            value = next;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private string NormalizeLegacyUsername(string rawUsername)
    {
        var candidate = string.IsNullOrWhiteSpace(rawUsername) ? "Player" : rawUsername.Trim();
        var sanitized = new StringBuilder(candidate.Length);

        foreach (var ch in candidate)
        {
            var isAsciiLetter = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
            var isDigit = ch >= '0' && ch <= '9';
            if (isAsciiLetter || isDigit || ch == '_')
            {
                sanitized.Append(ch);
            }
            else
            {
                sanitized.Append('_');
            }
        }

        var normalized = sanitized.ToString();
        if (normalized.Length > 16)
        {
            normalized = normalized[..16];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Player";
        }

        if (!normalized.Equals(candidate, StringComparison.Ordinal))
        {
            logService.LogInfo($"Legacy username normalized from '{candidate}' to '{normalized}'.");
        }

        return normalized;
    }

    private string PrepareLegacyCompatibilityHome(string instanceDirectory)
    {
        var profileName = new DirectoryInfo(instanceDirectory).Name;
        var fullInstancePath = Path.GetFullPath(instanceDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullInstancePath))).ToLowerInvariant();
        var shortHash = hash[..12];
        var localDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localDataRoot))
        {
            localDataRoot = Path.GetTempPath();
        }

        var projectDirectoryName = _settingsService.GetProjectDirectoryName();
        var legacyHome = Path.Combine(localDataRoot, projectDirectoryName, "legacy-home", $"{profileName}-{shortHash}");

        var legacyRoot = Path.Combine(legacyHome, ".minecraft");
        Directory.CreateDirectory(legacyRoot);
        EnsureLegacyMinecraftProjection(instanceDirectory, legacyRoot);

        EnsureLegacyForgeDeobfMap(instanceDirectory, legacyRoot);
        return legacyHome;
    }

    private void EnsureLegacyMinecraftProjection(string instanceDirectory, string legacyMinecraftRoot)
    {
        foreach (var dirName in new[]
                 {
                     "lib",
                     "libraries",
                     "mods",
                     "coremods",
                     "config",
                     "resources",
                     "texturepacks",
                     "shaderpacks",
                     "natives",
                     "assets"
                 })
        {
            var sourceDir = Path.Combine(instanceDirectory, dirName);
            var projectedDir = Path.Combine(legacyMinecraftRoot, dirName);
            if (Directory.Exists(sourceDir))
            {
                EnsureDirectoryProjection(projectedDir, sourceDir);
                continue;
            }

            RemoveProjectedPath(projectedDir);
        }

        var sourceMinecraftJar = Path.Combine(instanceDirectory, "minecraft.jar");
        var projectedMinecraftJar = Path.Combine(legacyMinecraftRoot, "minecraft.jar");
        if (File.Exists(sourceMinecraftJar))
        {
            CopyFileIfChanged(sourceMinecraftJar, projectedMinecraftJar);
            return;
        }

        RemoveProjectedPath(projectedMinecraftJar);
    }

    private void EnsureDirectoryProjection(string projectedDir, string sourceDir)
    {
        if (Directory.Exists(projectedDir) && IsReparsePoint(projectedDir))
        {
            return;
        }

        if (Directory.Exists(projectedDir))
        {
            MirrorDirectorySnapshot(sourceDir, projectedDir);
            return;
        }

        if (File.Exists(projectedDir))
        {
            File.Delete(projectedDir);
        }

        try
        {
            Directory.CreateSymbolicLink(projectedDir, sourceDir);
        }
        catch (Exception ex)
        {
            // Symlink creation may fail on locked-down Windows policies; mirror files as fallback.
            Directory.CreateDirectory(projectedDir);
            MirrorDirectorySnapshot(sourceDir, projectedDir);
            logService.LogInfo($"Legacy Forge support: projection fallback for '{projectedDir}': {ex.Message}");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void MirrorDirectorySnapshot(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        var sourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceSubDir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourceSubDir);
            var normalizedRelative = NormalizePath(relative);
            sourceDirectories.Add(normalizedRelative);
            var targetSubDir = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(targetSubDir);
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourceFile);
            var normalizedRelative = NormalizePath(relative);
            sourceFiles.Add(normalizedRelative);
            var targetFile = Path.Combine(targetDir, relative);
            CopyFileIfChanged(sourceFile, targetFile);
        }

        foreach (var targetFile in Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizePath(Path.GetRelativePath(targetDir, targetFile));
            if (sourceFiles.Contains(relative))
            {
                continue;
            }

            RemoveProjectedPath(targetFile);
        }

        var targetDirectories = Directory
            .EnumerateDirectories(targetDir, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();
        foreach (var targetSubDir in targetDirectories)
        {
            var relative = NormalizePath(Path.GetRelativePath(targetDir, targetSubDir));
            if (sourceDirectories.Contains(relative))
            {
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(targetSubDir).Any())
            {
                continue;
            }

            RemoveProjectedPath(targetSubDir);
        }
    }

    private static void CopyFileIfChanged(string sourceFile, string targetFile)
    {
        var targetDirectory = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        if (!File.Exists(targetFile))
        {
            File.Copy(sourceFile, targetFile, overwrite: true);
            return;
        }

        var sourceInfo = new FileInfo(sourceFile);
        var targetInfo = new FileInfo(targetFile);
        if (sourceInfo.Length != targetInfo.Length ||
            sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc.AddSeconds(1))
        {
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static void RemoveProjectedPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                Directory.Delete(path, recursive: !isReparsePoint);
                return;
            }

            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for legacy projection.
        }
    }

    private void EnsureLegacyForgeDeobfMap(string instanceDirectory, string legacyMinecraftRoot)
    {
        var primaryLibDirectory = Path.Combine(instanceDirectory, "lib");
        var secondaryLibDirectory = Path.Combine(legacyMinecraftRoot, "lib");
        Directory.CreateDirectory(primaryLibDirectory);
        Directory.CreateDirectory(secondaryLibDirectory);

        var existingPrimary = Directory
            .EnumerateFiles(primaryLibDirectory, "deobfuscation_data_*.zip", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        var existingSecondary = Directory
            .EnumerateFiles(secondaryLibDirectory, "deobfuscation_data_*.zip", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(existingPrimary) && !string.IsNullOrWhiteSpace(existingSecondary))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(existingPrimary))
        {
            var fileName = Path.GetFileName(existingPrimary);
            var secondaryTarget = Path.Combine(secondaryLibDirectory, fileName);
            File.Copy(existingPrimary, secondaryTarget, overwrite: true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(existingSecondary))
        {
            var fileName = Path.GetFileName(existingSecondary);
            var primaryTarget = Path.Combine(primaryLibDirectory, fileName);
            File.Copy(existingSecondary, primaryTarget, overwrite: true);
            return;
        }

        var normalizedPrimaryLibDirectory = Path.GetFullPath(primaryLibDirectory);
        var normalizedSecondaryLibDirectory = Path.GetFullPath(secondaryLibDirectory);
        var source = Directory
            .EnumerateFiles(instanceDirectory, "deobfuscation_data_*.zip", SearchOption.AllDirectories)
            .Where(path =>
            {
                var fullPath = Path.GetFullPath(path);
                return !fullPath.StartsWith(normalizedPrimaryLibDirectory, StringComparison.OrdinalIgnoreCase) &&
                       !fullPath.StartsWith(normalizedSecondaryLibDirectory, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path.Contains("forge", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.Length)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(source))
        {
            logService.LogInfo(
                "Legacy Forge support: deobfuscation_data_*.zip was not found in instance files. " +
                "FML may fail until this file is present.");
            return;
        }

        var sourceFileName = Path.GetFileName(source);
        var sourcePrimaryTarget = Path.Combine(primaryLibDirectory, sourceFileName);
        var sourceSecondaryTarget = Path.Combine(secondaryLibDirectory, sourceFileName);
        File.Copy(source, sourcePrimaryTarget, overwrite: true);
        File.Copy(source, sourceSecondaryTarget, overwrite: true);

        var primaryRelative = NormalizePath(Path.GetRelativePath(instanceDirectory, sourcePrimaryTarget));
        var secondaryRelative = NormalizePath(Path.GetRelativePath(instanceDirectory, sourceSecondaryTarget));
        logService.LogInfo($"Legacy Forge support: prepared {primaryRelative} and {secondaryRelative}.");
    }

    private static void AppendRouteArgs(ICollection<string> gameArgs, string address, int port)
    {
        if (gameArgs is List<string> argsList)
        {
            RemoveArgWithValue(argsList, "--server");
            RemoveArgWithValue(argsList, "--port");
            argsList.Add("--server");
            argsList.Add(address.Trim());
            argsList.Add("--port");
            argsList.Add(port.ToString(CultureInfo.InvariantCulture));
            return;
        }

        gameArgs.Add("--server");
        gameArgs.Add(address.Trim());
        gameArgs.Add("--port");
        gameArgs.Add(port.ToString(CultureInfo.InvariantCulture));
    }

    private static void RemoveArgWithValue(List<string> args, string key)
    {
        var prefix = key + "=";
        var index = 0;
        while (index < args.Count)
        {
            var token = args[index];
            if (token.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                args.RemoveAt(index);
                if (index < args.Count && !args[index].StartsWith("-", StringComparison.Ordinal))
                {
                    args.RemoveAt(index);
                }

                continue;
            }

            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                args.RemoveAt(index);
                continue;
            }

            index++;
        }
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
