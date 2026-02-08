using BivLauncher.Client.Models;
using System.Diagnostics;
using System.Text;
using System.Globalization;
using System.IO.Compression;

namespace BivLauncher.Client.Services;

public sealed class GameLaunchService(ILogService logService) : IGameLaunchService
{
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

        var gameArgs = SplitArgs(manifest.GameArgsDefault).ToList();
        var disableAutoRouteArgs = StripControlFlag(gameArgs, "--bl-no-route");
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
        if (launchMode == "mainclass")
        {
            var launchMainClass = manifest.LaunchMainClass.Trim();
            if (string.IsNullOrWhiteSpace(launchMainClass))
            {
                throw new InvalidOperationException("Manifest launchMainClass is required for mainclass mode.");
            }

            var classpath = ResolveClasspath(manifest, instanceDirectory, route.PreferredJarPath);
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(classpath);
            startInfo.ArgumentList.Add(launchMainClass);
        }
        else
        {
            var gameJar = ResolveGameJar(manifest, instanceDirectory, route.PreferredJarPath);
            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(gameJar);
        }

        foreach (var arg in gameArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        logService.LogInfo($"Process started: {startInfo.FileName} {BuildArgsPreview(startInfo.ArgumentList)}");

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

        await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync(cancellationToken));
        logService.LogInfo($"Process exited with code {process.ExitCode}");

        return new LaunchResult
        {
            ExitCode = process.ExitCode,
            JavaExecutable = javaExecutable
        };
    }

    private static async Task PumpOutputAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            onLine(line);
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
                if (TryValidateJarArchive(preferredAbsolutePath, requireMainClass: true, out var preferredReason))
                {
                    return preferredAbsolutePath;
                }

                logService.LogInfo(
                    $"Selected route jar '{normalizedPreferred}' is not launchable: {preferredReason}. Trying fallback jar.");

                var fallbackByPreferred = ResolveBestJarCandidate(
                    manifest,
                    instanceDirectory,
                    preferredFileName: Path.GetFileName(normalizedPreferred),
                    excludedAbsolutePath: preferredAbsolutePath,
                    requireMainClass: true,
                    out var fallbackReason);
                if (!string.IsNullOrWhiteSpace(fallbackByPreferred))
                {
                    var resolvedRelative = NormalizePath(Path.GetRelativePath(instanceDirectory, fallbackByPreferred));
                    logService.LogInfo($"Fallback jar selected: {resolvedRelative}");
                    return fallbackByPreferred;
                }

                throw new InvalidDataException(
                    $"Selected route jar '{normalizedPreferred}' is not launchable ({preferredReason}). " +
                    $"No fallback launchable jar found. Details: {fallbackReason}");
            }

            var preferredFileName = Path.GetFileName(normalizedPreferred);
            var fallbackByMissingPreferred = ResolveBestJarCandidate(
                manifest,
                instanceDirectory,
                preferredFileName,
                excludedAbsolutePath: string.Empty,
                requireMainClass: true,
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
            requireMainClass: true,
            out var autoResolveReason);
        if (!string.IsNullOrWhiteSpace(autoResolved))
        {
            return autoResolved;
        }

        throw new InvalidOperationException(
            $"No launchable .jar file found in manifest or instance directory. Details: {autoResolveReason}");
    }

    private static string ResolveClasspath(LauncherManifest manifest, string instanceDirectory, string preferredJarPath)
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

        return string.Join(Path.PathSeparator, resolved);
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
            if (!manifestPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
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
            foreach (var absolutePath in Directory.EnumerateFiles(instanceDirectory, "*.jar", SearchOption.AllDirectories))
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
            if (requireMainClass &&
                !IsJarEligibleForJarMode(candidate.RelativePath, normalizedPreferredFileName, candidate.AbsolutePath))
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
            ? "No .jar candidates found."
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

        if (string.Equals(fileName, "minecraft.jar", StringComparison.OrdinalIgnoreCase))
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
        var index = args.FindIndex(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        while (index >= 0)
        {
            args.RemoveAt(index);
            if (index < args.Count && !args[index].StartsWith("-", StringComparison.Ordinal))
            {
                args.RemoveAt(index);
            }

            index = args.FindIndex(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
