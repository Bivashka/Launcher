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

        return new LaunchResult { ExitCode = process.ExitCode };
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

    private static string ResolveGameJar(LauncherManifest manifest, string instanceDirectory, string preferredJarPath)
    {
        var normalizedPreferred = preferredJarPath.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            var preferredAbsolutePath = Path.Combine(instanceDirectory, normalizedPreferred.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(preferredAbsolutePath))
            {
                throw new FileNotFoundException("Selected route minecraft.jar does not exist in instance directory.", preferredAbsolutePath);
            }

            return preferredAbsolutePath;
        }

        var jarEntry = manifest.Files
            .Select(x => x.Path)
            .FirstOrDefault(path =>
                path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                path.Contains("minecraft", StringComparison.OrdinalIgnoreCase))
            ?? manifest.Files
                .Select(x => x.Path)
                .FirstOrDefault(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(jarEntry))
        {
            throw new InvalidOperationException("No .jar file was found in manifest. Cannot launch.");
        }

        var jarPath = Path.Combine(instanceDirectory, jarEntry.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(jarPath))
        {
            throw new FileNotFoundException("Game jar file does not exist in instance directory.", jarPath);
        }

        return jarPath;
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

        var selectedRouteJar = TryResolveRouteJar(instanceDirectory, preferredJarPath);
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

    private static string TryResolveRouteJar(string instanceDirectory, string preferredJarPath)
    {
        var normalized = preferredJarPath.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var path = Path.Combine(instanceDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : string.Empty;
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
