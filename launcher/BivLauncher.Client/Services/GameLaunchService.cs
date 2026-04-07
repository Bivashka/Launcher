using BivLauncher.Client.Models;
using System.Diagnostics;
using System.Text;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Net.Http;
using System.Reflection;

namespace BivLauncher.Client.Services;

public sealed class GameLaunchService(
    ILogService logService,
    ISettingsService settingsService,
    ILauncherApiService launcherApiService) : IGameLaunchService
{
    private readonly ISettingsService _settingsService = settingsService;
    private readonly ILauncherApiService _launcherApiService = launcherApiService;

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
    private const string DefaultClientAuthlibAssetPath = "/api/public/assets/uploads/assets/launcher.jar";
    private const string LocalClientAuthlibRelativePath = ".bivlauncher/authlib-injector.jar";
    private const string LegacySessionDomainCompatibilityMarker = "authserver.mojang.com";
    private static readonly string[] LegacySessionDomainCompatibilityMarkers =
    [
        "authserver.mojang.com",
        "sessionserver.mojang.com",
        "session.minecraft.net"
    ];
    private static readonly SemaphoreSlim LegacyJavaRuntimeDownloadLock = new(1, 1);
    private const string LegacyJavaRuntimeCacheDirectoryName = "java-runtime";
    private const string LegacyJavaRuntimeCacheSlot = "win-x64-jre8";
    private const string DefaultLegacyJavaRuntimeWinX64Url = "https://api.adoptium.net/v3/binary/latest/8/ga/windows/x64/jre/hotspot/normal/eclipse";

    public async Task<LaunchResult> LaunchAsync(
        LauncherManifest manifest,
        LauncherSettings settings,
        GameLaunchRoute route,
        string instanceDirectory,
        Action<string> onProcessLine,
        Action<int>? onProcessStarted = null,
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

        var usePositionalLegacyArgs = ShouldUseLegacyPositionalArguments(route, manifest, instanceDirectory);
        var javaExecutable = await ResolveJavaExecutableAsync(
            settings.JavaMode,
            manifest.JavaRuntime,
            instanceDirectory,
            requireLegacyJava8Compatibility: usePositionalLegacyArgs,
            cancellationToken);
        logService.LogInfo(
            $"Java executable selected: {DescribeJavaExecutableForLog(javaExecutable, instanceDirectory)} (mode={settings.JavaMode}, legacyCompat={usePositionalLegacyArgs}).");
        var jvmArgs = SplitArgs(manifest.JvmArgsDefault)
            .Where(x => !x.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) &&
                        !x.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
            .ToList();

        jvmArgs.Insert(0, $"-Xmx{settings.RamMb}M");
        jvmArgs.Insert(0, "-Xms1024M");
        await TryAttachClientAuthlibAgentAsync(
            jvmArgs,
            settings,
            instanceDirectory,
            requireLegacySessionDomainCompatibility: usePositionalLegacyArgs,
            cancellationToken);
        logService.LogInfo(
            $"Legacy positional mode: {usePositionalLegacyArgs} (routeMcVersion='{route.McVersion}', manifestMcVersion='{manifest.McVersion}').");
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
                EnsureLegacyBridgeJvmProperties(startInfo.ArgumentList, settings);
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
        onProcessStarted?.Invoke(process.Id);

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
            var tail = LogSanitizer.Sanitize(pending.ToString().Trim());
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
                var line = LogSanitizer.Sanitize(pending.ToString(start, length).Trim());
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

    private async Task<string> ResolveJavaExecutableAsync(
        string javaMode,
        string? javaRuntime,
        string instanceDirectory,
        bool requireLegacyJava8Compatibility,
        CancellationToken cancellationToken)
    {
        var bundledJavaExecutable = ResolveBundledJavaExecutableOrEmpty(javaRuntime, instanceDirectory);
        var systemJavaExecutable = ResolveSystemJavaExecutableOrEmpty();
        var bundledJavaMajorVersion = ResolveJavaMajorVersion(bundledJavaExecutable);
        var systemJavaMajorVersion = ResolveJavaMajorVersion(systemJavaExecutable);

        if (javaMode.Equals("Bundled", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(bundledJavaExecutable))
            {
                EnsureJavaCompatibility(
                    bundledJavaExecutable,
                    bundledJavaMajorVersion,
                    requireLegacyJava8Compatibility,
                    javaMode);
                return bundledJavaExecutable;
            }

            if (requireLegacyJava8Compatibility)
            {
                var downloadedLegacyJavaExecutable = await EnsureLegacyBundledJavaRuntimeAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(downloadedLegacyJavaExecutable))
                {
                    var downloadedLegacyJavaMajorVersion = ResolveJavaMajorVersion(downloadedLegacyJavaExecutable);
                    EnsureJavaCompatibility(
                        downloadedLegacyJavaExecutable,
                        downloadedLegacyJavaMajorVersion,
                        requireLegacyJava8Compatibility,
                        javaMode);
                    return downloadedLegacyJavaExecutable;
                }
            }

            if (string.IsNullOrWhiteSpace(javaRuntime))
            {
                throw new InvalidOperationException("Bundled Java mode selected but no bundled runtime is available.");
            }

            var runtimePath = Path.Combine(instanceDirectory, javaRuntime.Replace('/', Path.DirectorySeparatorChar));
            throw new FileNotFoundException("Bundled Java runtime executable is missing.", runtimePath);
        }

        if (javaMode.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(systemJavaExecutable))
            {
                EnsureJavaCompatibility(
                    systemJavaExecutable,
                    systemJavaMajorVersion,
                    requireLegacyJava8Compatibility,
                    javaMode);
                return systemJavaExecutable;
            }

            if (requireLegacyJava8Compatibility)
            {
                throw new InvalidOperationException(
                    "Legacy client requires Java 8 or older, but system Java was not found. Install Java 8 or switch to Bundled mode with a Java 8 runtime.");
            }

            return "java";
        }

        // Keep Auto mode system-first. Some legacy clients rely on the machine JRE
        // for TLS/certificate compatibility during authlib/Yggdrasil server login.
        if (requireLegacyJava8Compatibility)
        {
            if (!string.IsNullOrWhiteSpace(systemJavaExecutable) &&
                IsJavaCompatibleWithLegacyLaunchwrapper(systemJavaMajorVersion))
            {
                return systemJavaExecutable;
            }

            if (!string.IsNullOrWhiteSpace(bundledJavaExecutable) &&
                IsJavaCompatibleWithLegacyLaunchwrapper(bundledJavaMajorVersion))
            {
                return bundledJavaExecutable;
            }

            var downloadedLegacyJavaExecutable = await EnsureLegacyBundledJavaRuntimeAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(downloadedLegacyJavaExecutable))
            {
                return downloadedLegacyJavaExecutable;
            }

            throw new InvalidOperationException(
                BuildLegacyJavaCompatibilityError(systemJavaExecutable, systemJavaMajorVersion, bundledJavaExecutable, bundledJavaMajorVersion));
        }

        if (!string.IsNullOrWhiteSpace(systemJavaExecutable))
        {
            return systemJavaExecutable;
        }

        return !string.IsNullOrWhiteSpace(bundledJavaExecutable)
            ? bundledJavaExecutable
            : "java";
    }

    private async Task<string> EnsureLegacyBundledJavaRuntimeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        var cacheRoot = Path.Combine(_settingsService.GetUpdatesDirectory(), LegacyJavaRuntimeCacheDirectoryName);
        var runtimeRoot = Path.Combine(cacheRoot, LegacyJavaRuntimeCacheSlot);
        var cachedJavaExecutable = TryFindJavaExecutableInDirectory(runtimeRoot);
        var cachedJavaMajorVersion = ResolveJavaMajorVersion(cachedJavaExecutable);
        if (!string.IsNullOrWhiteSpace(cachedJavaExecutable) &&
            IsJavaCompatibleWithLegacyLaunchwrapper(cachedJavaMajorVersion))
        {
            logService.LogInfo($"Legacy Java runtime cache hit: {NormalizePath(cachedJavaExecutable)}.");
            return cachedJavaExecutable;
        }

        await LegacyJavaRuntimeDownloadLock.WaitAsync(cancellationToken);
        try
        {
            cachedJavaExecutable = TryFindJavaExecutableInDirectory(runtimeRoot);
            cachedJavaMajorVersion = ResolveJavaMajorVersion(cachedJavaExecutable);
            if (!string.IsNullOrWhiteSpace(cachedJavaExecutable) &&
                IsJavaCompatibleWithLegacyLaunchwrapper(cachedJavaMajorVersion))
            {
                logService.LogInfo($"Legacy Java runtime cache hit: {NormalizePath(cachedJavaExecutable)}.");
                return cachedJavaExecutable;
            }

            Directory.CreateDirectory(cacheRoot);
            var tempDirectory = Path.Combine(cacheRoot, $"{LegacyJavaRuntimeCacheSlot}.tmp-{Guid.NewGuid():N}");
            var archivePath = Path.Combine(cacheRoot, $"{LegacyJavaRuntimeCacheSlot}.zip");

            TryDeleteDirectory(tempDirectory);
            Directory.CreateDirectory(tempDirectory);

            logService.LogInfo($"Legacy Java runtime auto-download started: {DefaultLegacyJavaRuntimeWinX64Url}");

            try
            {
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(10)
                };

                using var response = await httpClient.GetAsync(
                    DefaultLegacyJavaRuntimeWinX64Url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var archiveStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await remoteStream.CopyToAsync(archiveStream, cancellationToken);
                }

                ExtractZipArchiveSafely(archivePath, tempDirectory);
                var extractedJavaExecutable = TryFindJavaExecutableInDirectory(tempDirectory);
                if (string.IsNullOrWhiteSpace(extractedJavaExecutable))
                {
                    throw new InvalidOperationException("Downloaded Java runtime archive does not contain javaw.exe/java.exe.");
                }

                TryDeleteDirectory(runtimeRoot);
                Directory.Move(tempDirectory, runtimeRoot);

                var resolvedJavaExecutable = TryFindJavaExecutableInDirectory(runtimeRoot);
                if (string.IsNullOrWhiteSpace(resolvedJavaExecutable))
                {
                    throw new InvalidOperationException("Downloaded Java runtime archive was extracted, but no executable was found.");
                }

                logService.LogInfo($"Legacy Java runtime auto-download completed: {NormalizePath(resolvedJavaExecutable)}.");
                return resolvedJavaExecutable;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to auto-download Java 8 runtime: {ex.Message}", ex);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
                TryDeleteFile(archivePath);
            }
        }
        finally
        {
            LegacyJavaRuntimeDownloadLock.Release();
        }
    }

    private static string ResolveBundledJavaExecutableOrEmpty(string? javaRuntime, string instanceDirectory)
    {
        if (!string.IsNullOrWhiteSpace(javaRuntime))
        {
            var runtimePath = Path.Combine(instanceDirectory, javaRuntime.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(runtimePath))
            {
                return runtimePath;
            }
        }

        return ResolveLauncherBundledJavaExecutableOrEmpty();
    }

    private static string ResolveSystemJavaExecutableOrEmpty()
    {
        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        var javaHome = (Environment.GetEnvironmentVariable("JAVA_HOME") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            try
            {
                var candidate = Path.Combine(javaHome.Trim('"'), "bin", executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawSegment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var segment = rawSegment.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                var candidate = Path.Combine(segment, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static string ResolveLauncherBundledJavaExecutableOrEmpty()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "launcher-data")
        };

        foreach (var root in searchRoots)
        {
            var fromRoot = TryFindJavaExecutableInLauncherRoot(root);
            if (!string.IsNullOrWhiteSpace(fromRoot))
            {
                return fromRoot;
            }
        }

        return string.Empty;
    }

    private static string TryFindJavaExecutableInDirectory(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "javaw.exe", "java.exe" }
            : new[] { "java" };
        var matches = executableNames
            .SelectMany(executableName =>
                Directory.EnumerateFiles(rootPath, executableName, SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.EndsWith($"{Path.DirectorySeparatorChar}javaw.exe", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count > 0 ? matches[0] : string.Empty;
    }

    private static string TryFindJavaExecutableInLauncherRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "javaw.exe", "java.exe" }
            : new[] { "java" };
        var candidateDirectories = new[]
        {
            Path.Combine(rootPath, "runtime"),
            Path.Combine(rootPath, "java"),
            Path.Combine(rootPath, "jre"),
            Path.Combine(rootPath, "launcher-runtime")
        };

        foreach (var candidateDirectory in candidateDirectories)
        {
            if (!Directory.Exists(candidateDirectory))
            {
                continue;
            }

            var matches = executableNames
                .SelectMany(executableName =>
                    Directory.EnumerateFiles(candidateDirectory, executableName, SearchOption.AllDirectories))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matches.Count > 0)
            {
                return matches[0];
            }
        }

        return string.Empty;
    }

    private static void EnsureJavaCompatibility(
        string javaExecutable,
        int javaMajorVersion,
        bool requireLegacyJava8Compatibility,
        string javaMode)
    {
        if (!requireLegacyJava8Compatibility ||
            string.IsNullOrWhiteSpace(javaExecutable) ||
            IsJavaCompatibleWithLegacyLaunchwrapper(javaMajorVersion))
        {
            return;
        }

        var displayVersion = javaMajorVersion > 0
            ? $"Java {javaMajorVersion}"
            : "an unknown Java version";
        throw new InvalidOperationException(
            $"Legacy client requires Java 8 or older, but {javaMode} mode selected {displayVersion}. Choose Java 8 or configure a Java 8 bundled runtime.");
    }

    private static bool IsJavaCompatibleWithLegacyLaunchwrapper(int javaMajorVersion)
    {
        return javaMajorVersion <= 0 || javaMajorVersion <= 8;
    }

    private static string BuildLegacyJavaCompatibilityError(
        string systemJavaExecutable,
        int systemJavaMajorVersion,
        string bundledJavaExecutable,
        int bundledJavaMajorVersion)
    {
        var details = new List<string>();

        if (!string.IsNullOrWhiteSpace(systemJavaExecutable))
        {
            details.Add($"system={FormatJavaCandidate(systemJavaExecutable, systemJavaMajorVersion)}");
        }

        if (!string.IsNullOrWhiteSpace(bundledJavaExecutable))
        {
            details.Add($"bundled={FormatJavaCandidate(bundledJavaExecutable, bundledJavaMajorVersion)}");
        }

        var suffix = details.Count == 0
            ? "No Java runtime was found."
            : $"Detected: {string.Join(", ", details)}.";
        return $"Legacy client requires Java 8 or older. {suffix} Install Java 8 or attach a Java 8 bundled runtime to the profile.";
    }

    private static string FormatJavaCandidate(string javaExecutable, int javaMajorVersion)
    {
        var label = javaMajorVersion > 0 ? $"Java {javaMajorVersion}" : "unknown version";
        return $"{label} ({javaExecutable})";
    }

    private static int ResolveJavaMajorVersion(string javaExecutable)
    {
        if (string.IsNullOrWhiteSpace(javaExecutable) || !Path.IsPathRooted(javaExecutable))
        {
            return 0;
        }

        try
        {
            var javaHome = Directory.GetParent(javaExecutable)?.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(javaHome))
            {
                return 0;
            }

            var releaseFilePath = Path.Combine(javaHome, "release");
            if (!File.Exists(releaseFilePath))
            {
                return 0;
            }

            foreach (var line in File.ReadLines(releaseFilePath))
            {
                if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal))
                {
                    continue;
                }

                var rawVersion = line["JAVA_VERSION=".Length..].Trim().Trim('"');
                return ParseJavaMajorVersion(rawVersion);
            }
        }
        catch
        {
        }

        return 0;
    }

    private static int ParseJavaMajorVersion(string rawVersion)
    {
        var normalized = (rawVersion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        if (normalized.StartsWith("1.", StringComparison.Ordinal))
        {
            normalized = normalized["1.".Length..];
        }

        var digits = new string(normalized.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var majorVersion) &&
               majorVersion > 0
            ? majorVersion
            : 0;
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
        var normalizedPreferredMainClass = preferredMainClass?.Trim() ?? string.Empty;

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

        if (ShouldPreferLaunchwrapperCandidate(classpathEntries, normalizedPreferredMainClass))
        {
            AddCandidate("net.minecraft.launchwrapper.Launch");
        }

        AddCandidate(normalizedPreferredMainClass);
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

    private static bool ShouldPreferLaunchwrapperCandidate(
        IReadOnlyList<string> classpathEntries,
        string preferredMainClass)
    {
        if (!string.Equals(preferredMainClass, "cpw.mods.modlauncher.Launcher", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!ContainsClass(classpathEntries, "net.minecraft.launchwrapper.Launch"))
        {
            return false;
        }

        return !classpathEntries.Any(path =>
        {
            var fileName = Path.GetFileName(path);
            return fileName.Contains("modlauncher", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase);
        });
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

    private static string DescribeJavaExecutableForLog(string javaExecutable, string instanceDirectory)
    {
        if (string.IsNullOrWhiteSpace(javaExecutable) || !Path.IsPathRooted(javaExecutable))
        {
            return javaExecutable;
        }

        try
        {
            var normalizedJavaPath = Path.GetFullPath(javaExecutable);
            var normalizedInstanceDirectory = Path.GetFullPath(instanceDirectory);
            if (normalizedJavaPath.StartsWith(normalizedInstanceDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePath(Path.GetRelativePath(instanceDirectory, normalizedJavaPath));
            }

            return NormalizePath(normalizedJavaPath);
        }
        catch
        {
            return NormalizePath(javaExecutable);
        }
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
        return LogSanitizer.Sanitize(string.Join(' ', args.Select(QuoteIfNeeded)));
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

    private void EnsureLegacyBridgeJvmProperties(IList<string> jvmArgs, LauncherSettings settings)
    {
        var rawUsername = (settings.PlayerAuthUsername ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawUsername))
        {
            return;
        }

        var username = NormalizeLegacyUsername(rawUsername);
        var token = (settings.PlayerAuthToken ?? string.Empty).Trim();
        var externalId = (settings.PlayerAuthExternalId ?? string.Empty).Trim();
        var profileId = ResolveLegacyProfileId(externalId, username);
        var sessionToken = string.IsNullOrWhiteSpace(token)
            ? string.Empty
            : BuildLegacySessionToken(token, profileId);
        var apiBaseUrl = ResolvePlayerAuthApiBaseUrl(settings);
        var yggdrasilUrl = BuildYggdrasilBaseUrl(apiBaseUrl);

        var insertionIndex = FindLaunchModeArgumentIndex(jvmArgs);
        insertionIndex = EnsureJvmProperty(jvmArgs, "biv.auth.username", username, insertionIndex);
        insertionIndex = EnsureJvmProperty(jvmArgs, "biv.auth.uuid", profileId, insertionIndex);
        insertionIndex = EnsureJvmProperty(jvmArgs, "biv.auth.externalId", externalId, insertionIndex);
        insertionIndex = EnsureJvmProperty(jvmArgs, "biv.auth.token", token, insertionIndex);
        insertionIndex = EnsureJvmProperty(jvmArgs, "biv.auth.session", sessionToken, insertionIndex);
        insertionIndex = EnsureJvmProperty(jvmArgs, "biv.auth.publicBaseUrl", apiBaseUrl, insertionIndex);
        _ = EnsureJvmProperty(jvmArgs, "biv.auth.yggdrasil", yggdrasilUrl, insertionIndex);

        logService.LogInfo(
            $"Legacy bridge JVM auth properties prepared: username={username}, tokenLength={token.Length}, profileIdLength={profileId.Length}, hasSession={!string.IsNullOrWhiteSpace(sessionToken)}, hasYggdrasil={!string.IsNullOrWhiteSpace(yggdrasilUrl)}, hasPublicBase={!string.IsNullOrWhiteSpace(apiBaseUrl)}.");
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

    private async Task TryAttachClientAuthlibAgentAsync(
        IList<string> jvmArgs,
        LauncherSettings settings,
        string instanceDirectory,
        bool requireLegacySessionDomainCompatibility,
        CancellationToken cancellationToken)
    {
        var rawToken = (settings.PlayerAuthToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var apiBaseUrl = ResolvePlayerAuthApiBaseUrl(settings);
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return;
        }

        var yggdrasilUrl = BuildYggdrasilBaseUrl(apiBaseUrl);
        if (string.IsNullOrWhiteSpace(yggdrasilUrl))
        {
            return;
        }

        var agentPath = await ResolveClientAuthlibAgentPathAsync(
            apiBaseUrl,
            instanceDirectory,
            requireLegacySessionDomainCompatibility,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(agentPath))
        {
            if (requireLegacySessionDomainCompatibility)
            {
                throw new InvalidOperationException(
                    $"Legacy authlib-injector compatibility is required ({LegacySessionDomainCompatibilityMarker}) but a compatible launcher.jar is unavailable.");
            }

            logService.LogInfo("WARN: Client authlib-injector is not available; continuing without -javaagent.");
            return;
        }

        var insertionIndex = FindLaunchModeArgumentIndex(jvmArgs);
        insertionIndex = EnsureJvmProperty(jvmArgs, "authlibinjector.noShowServerName", "true", insertionIndex);
        _ = EnsureJavaAgent(jvmArgs, agentPath, yggdrasilUrl, insertionIndex);

        logService.LogInfo(
            $"Client authlib-injector enabled: {NormalizePath(agentPath)} (requireLegacyCompat={requireLegacySessionDomainCompatibility}, hasYggdrasil=true).");
    }

    private async Task<string> ResolveClientAuthlibAgentPathAsync(
        string apiBaseUrl,
        string instanceDirectory,
        bool requireLegacySessionDomainCompatibility,
        CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(instanceDirectory, LocalClientAuthlibRelativePath);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        try
        {
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(targetDirectory ?? targetPath, ex, "create the authlib cache directory");
        }

        if (TryValidateAuthlibAgentJar(targetPath, requireLegacySessionDomainCompatibility, out _))
        {
            return Path.GetFullPath(targetPath);
        }

        try
        {
            await using var remoteStream = await _launcherApiService.OpenAssetReadStreamAsync(
                apiBaseUrl,
                DefaultClientAuthlibAssetPath,
                cancellationToken);
            EnsureWritableFile(targetPath);
            await using var localStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await remoteStream.CopyToAsync(localStream, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CreateFileAccessException(targetPath, ex, "write authlib-injector");
        }
        catch (Exception ex)
        {
            logService.LogInfo($"WARN: Client authlib-injector download failed: {ex.Message}");
        }

        if (TryValidateAuthlibAgentJar(targetPath, requireLegacySessionDomainCompatibility, out var downloadedValidationError))
        {
            return Path.GetFullPath(targetPath);
        }

        if (File.Exists(targetPath))
        {
            try
            {
                EnsureWritableFile(targetPath);
                File.Delete(targetPath);
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(downloadedValidationError))
        {
            logService.LogInfo($"WARN: Downloaded client authlib-injector is incompatible: {downloadedValidationError}.");
        }

        var fallbackCandidates = new[]
        {
            Path.Combine(instanceDirectory, "Launcher.jar"),
            Path.Combine(instanceDirectory, "launcher.jar"),
            Path.Combine(instanceDirectory, "libraries", "authlib-injector.jar"),
            Path.Combine(instanceDirectory, "libraries", "Launcher.jar"),
            Path.Combine(instanceDirectory, "libraries", "launcher.jar")
        };

        foreach (var candidate in fallbackCandidates)
        {
            if (TryValidateAuthlibAgentJar(candidate, requireLegacySessionDomainCompatibility, out _))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return string.Empty;
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

    private bool TryValidateAuthlibAgentJar(
        string jarPath,
        bool requireLegacySessionDomainCompatibility,
        out string validationError)
    {
        validationError = string.Empty;
        if (!TryValidateJarArchive(jarPath, requireMainClass: false, out var jarError))
        {
            validationError = string.IsNullOrWhiteSpace(jarError) ? "invalid jar archive" : jarError;
            return false;
        }

        if (!requireLegacySessionDomainCompatibility)
        {
            return true;
        }

        if (HasLegacySessionDomainCompatibility(jarPath))
        {
            return true;
        }

        validationError = $"missing legacy auth marker '{LegacySessionDomainCompatibilityMarker}'";
        return false;
    }

    private static bool HasLegacySessionDomainCompatibility(string jarPath)
    {
        if (string.IsNullOrWhiteSpace(jarPath) || !File.Exists(jarPath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(jarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var markers = LegacySessionDomainCompatibilityMarkers
                .Select(Encoding.ASCII.GetBytes)
                .ToArray();
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);
                var classBytes = memory.ToArray();
                if (markers.Any(marker => ContainsByteSequence(classBytes, marker)))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool ContainsByteSequence(byte[] haystack, byte[] needle)
    {
        if (haystack.Length == 0 || needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[index + offset] == needle[offset])
                {
                    continue;
                }

                matched = false;
                break;
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolvePlayerAuthApiBaseUrl(LauncherSettings settings)
    {
        var apiBaseUrl = (settings.PlayerAuthApiBaseUrl ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return apiBaseUrl.TrimEnd('/');
        }

        apiBaseUrl = (settings.ApiBaseUrl ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return apiBaseUrl.TrimEnd('/');
        }

        apiBaseUrl = ResolveConfiguredLauncherApiBaseUrl();
        return string.IsNullOrWhiteSpace(apiBaseUrl)
            ? string.Empty
            : apiBaseUrl.TrimEnd('/');
    }

    private static string ResolveConfiguredLauncherApiBaseUrl()
    {
        var environmentBaseUrl = NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable("BIVLAUNCHER_API_BASE_URL"));
        if (!string.IsNullOrWhiteSpace(environmentBaseUrl))
        {
            return environmentBaseUrl;
        }

        var assembly = Assembly.GetEntryAssembly() ?? typeof(GameLaunchService).Assembly;
        var bundledBaseUrl = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                "BivLauncher.ApiBaseUrl",
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        return NormalizeBaseUrlOrEmpty(bundledBaseUrl);
    }

    private static string NormalizeBaseUrlOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().TrimEnd('/');
    }

    private static string BuildYggdrasilBaseUrl(string apiBaseUrl)
    {
        var normalized = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized + "/api/public/yggdrasil";
    }

    private static int EnsureJavaAgent(IList<string> args, string agentJarPath, string yggdrasilUrl, int insertionIndex)
    {
        for (var i = args.Count - 1; i >= 0; i--)
        {
            if (!args[i].StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i < insertionIndex)
            {
                insertionIndex--;
            }

            args.RemoveAt(i);
        }

        if (string.IsNullOrWhiteSpace(agentJarPath) || string.IsNullOrWhiteSpace(yggdrasilUrl))
        {
            return insertionIndex;
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, args.Count);
        args.Insert(insertionIndex, $"-javaagent:{agentJarPath}={yggdrasilUrl}");
        return insertionIndex + 1;
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
        var rawUsername = (settings.PlayerAuthUsername ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawUsername))
        {
            throw new InvalidOperationException("Player auth username is missing. Re-login is required.");
        }

        var username = NormalizeLegacyUsername(rawUsername);

        var sessionToken = (settings.PlayerAuthToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            throw new InvalidOperationException("Player auth token is missing. Re-login is required.");
        }

        var externalId = (settings.PlayerAuthExternalId ?? string.Empty).Trim();
        var legacyProfileId = ResolveLegacyProfileId(externalId, username);
        var legacySessionToken = BuildLegacySessionToken(sessionToken, legacyProfileId);

        // Always keep named args as fallback. Some launchwrapper forks
        // ignore positional auth arguments even on old modpacks.
        EnsureArgumentWithValue(gameArgs, "--username", username);
        EnsureArgumentWithValue(gameArgs, "--session", legacySessionToken);
        if (!string.IsNullOrWhiteSpace(legacyProfileId))
        {
            EnsureArgumentWithValue(gameArgs, "--uuid", legacyProfileId);
        }

        if (usePositionalLegacyArgs)
        {
            // Pre-1.6 clients expect positional args: username, session, server, port.
            gameArgs.Insert(0, legacySessionToken);
            gameArgs.Insert(0, username);

            logService.LogInfo(
                $"Legacy auth args prepared (hybrid positional+named): username={username}, sourceUsername={rawUsername}, sessionTokenLength={sessionToken.Length}, sessionMode=token-profile, hasUuid={!string.IsNullOrWhiteSpace(legacyProfileId)}.");
            return;
        }

        logService.LogInfo(
            $"Legacy auth args prepared: username={username}, sourceUsername={rawUsername}, sessionTokenLength={sessionToken.Length}, sessionMode=token-profile, hasUuid={!string.IsNullOrWhiteSpace(legacyProfileId)}.");
    }

    private static string BuildLegacySessionToken(string accessToken, string profileId)
    {
        var token = (accessToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        if (token.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
        {
            return token;
        }

        return string.IsNullOrWhiteSpace(profileId)
            ? token
            : $"token:{token}:{profileId}";
    }

    private static string ResolveLegacyProfileId(string externalId, string username)
    {
        var candidate = (externalId ?? string.Empty).Trim();
        if (Guid.TryParse(candidate, out var parsedGuid))
        {
            return parsedGuid.ToString("N");
        }

        var hexOnly = new string(candidate.Where(IsHexChar).ToArray());
        if (hexOnly.Length == 32)
        {
            return hexOnly.ToLowerInvariant();
        }

        // Legacy session bridges commonly expect a 32-hex profile id.
        var source = "OfflinePlayer:" + (string.IsNullOrWhiteSpace(username) ? "Player" : username.Trim());
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(source));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var offlineGuid = new Guid(hash);
        return offlineGuid.ToString("N");
    }

    private static bool IsHexChar(char ch)
    {
        return (ch >= '0' && ch <= '9') ||
               (ch >= 'a' && ch <= 'f') ||
               (ch >= 'A' && ch <= 'F');
    }

    private void EnsureLegacyRouteArguments(
        List<string> gameArgs,
        GameLaunchRoute route,
        bool routeArgsExplicitlyDisabled,
        bool usePositionalLegacyArgs)
    {
        // Keep named route args as fallback even when positional mode is active.
        if (!routeArgsExplicitlyDisabled)
        {
            EnsureArgumentWithValue(gameArgs, "--server", route.Address.Trim());
            EnsureArgumentWithValue(gameArgs, "--port", route.Port.ToString(CultureInfo.InvariantCulture));
        }

        if (usePositionalLegacyArgs)
        {
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
        var routeVersion = NormalizeVersion(route.McVersion);
        var manifestVersion = NormalizeVersion(manifest.McVersion);

        // Treat "legacy" only as an explicit opt-in from route/manifest metadata.
        // Unknown version must not force positional args because it breaks 1.6+ clients.
        if (string.Equals(routeVersion, "legacy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifestVersion, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var inferredVersion = TryInferVersionFromDeobfMap(instanceDirectory);
        var resolvedVersion = !string.IsNullOrWhiteSpace(routeVersion)
            ? routeVersion
            : !string.IsNullOrWhiteSpace(manifestVersion)
                ? manifestVersion
                : inferredVersion;

        if (string.IsNullOrWhiteSpace(resolvedVersion) ||
            !TryParseMajorMinorVersion(resolvedVersion, out var major, out var minor))
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

        var parts = rawVersion
            .Trim()
            .Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var parsed = new List<int>(capacity: 2);
        foreach (var part in parts)
        {
            var digits = ExtractFirstDigitSequence(part);
            if (string.IsNullOrWhiteSpace(digits))
            {
                continue;
            }

            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            parsed.Add(value);
            if (parsed.Count == 2)
            {
                break;
            }
        }

        if (parsed.Count < 2)
        {
            return false;
        }

        major = parsed[0];
        minor = parsed[1];
        return true;
    }

    private static string ExtractFirstDigitSequence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsDigit(value[index]))
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            return string.Empty;
        }

        var end = start;
        while (end < value.Length && char.IsDigit(value[end]))
        {
            end++;
        }

        return value[start..end];
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

    private static void ExtractZipArchiveSafely(string archivePath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var normalizedDestination = Path.GetFullPath(destinationDirectory);
        var normalizedDestinationWithSeparator = normalizedDestination.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? normalizedDestination
            : normalizedDestination + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(normalizedDestination);

        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(normalizedDestination, entry.FullName));
            if (!destinationPath.StartsWith(normalizedDestinationWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destinationPath, normalizedDestination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Downloaded Java runtime archive contains an invalid path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            using var sourceStream = entry.Open();
            using var targetStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            sourceStream.CopyTo(targetStream);
        }
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
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
