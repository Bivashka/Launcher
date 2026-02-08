using BivLauncher.Client.Models;
using System.Diagnostics;
using System.Text;
using System.Globalization;

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
        AppendRouteArgs(gameArgs, route.Address, route.Port);

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
                return preferredAbsolutePath;
            }

            var preferredFileName = Path.GetFileName(normalizedPreferred);
            var fallbackByPreferred = ResolveBestJarCandidate(manifest, instanceDirectory, preferredFileName);
            if (!string.IsNullOrWhiteSpace(fallbackByPreferred))
            {
                var resolvedRelative = NormalizePath(Path.GetRelativePath(instanceDirectory, fallbackByPreferred));
                logService.LogInfo(
                    $"Selected route jar '{normalizedPreferred}' was not found. Fallback jar: {resolvedRelative}");
                return fallbackByPreferred;
            }

            throw new FileNotFoundException(
                $"Selected route jar '{normalizedPreferred}' does not exist in instance directory.",
                preferredAbsolutePath);
        }

        var autoResolved = ResolveBestJarCandidate(manifest, instanceDirectory, preferredFileName: string.Empty);
        if (!string.IsNullOrWhiteSpace(autoResolved))
        {
            return autoResolved;
        }

        throw new InvalidOperationException("No launchable .jar file found in manifest or instance directory.");
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
        if (File.Exists(path))
        {
            return path;
        }

        var preferredFileName = Path.GetFileName(normalized);
        return ResolveBestJarCandidate(manifest, instanceDirectory, preferredFileName);
    }

    private static string ResolveBestJarCandidate(
        LauncherManifest manifest,
        string instanceDirectory,
        string preferredFileName)
    {
        var normalizedPreferredFileName = string.IsNullOrWhiteSpace(preferredFileName)
            ? string.Empty
            : preferredFileName.Trim();

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

                var normalizedRelativePath = NormalizePath(Path.GetRelativePath(instanceDirectory, absolutePath));
                candidates.Add((normalizedRelativePath, absolutePath, ScoreJarCandidate(normalizedRelativePath, normalizedPreferredFileName, fromManifest: false)));
            }
        }

        return candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.RelativePath.Length)
            .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.AbsolutePath)
            .FirstOrDefault() ?? string.Empty;
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
