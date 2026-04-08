using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BivLauncher.Client.Services;

[SupportedOSPlatform("windows")]
public sealed class LauncherTamperMonitor : ILauncherTamperMonitor
{
    private static readonly HashSet<string> SuspiciousProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cheatengine",
        "cheatengine-x86_64",
        "cheatengine-i386",
        "x64dbg",
        "x32dbg",
        "ollydbg",
        "ida",
        "ida64",
        "idaq",
        "idag",
        "dnspy",
        "ilspy",
        "processhacker",
        "procexp",
        "frida",
        "frida-helper",
        "frida-server",
        "scylla",
        "megadumper",
        "ksdumperclient"
    };

    private static readonly string[] SuspiciousModuleNameTokens =
    [
        "cheatengine",
        "speedhack",
        "x64dbg",
        "x32dbg",
        "ollydbg",
        "easyhook",
        "detours",
        "minhook",
        "frida",
        "scylla",
        "megadumper",
        "ksdumper"
    ];

    private static readonly string[] SuspiciousCommandLineTokens =
    [
        "com.sun.management.util.managementutil",
        "com.sun.management.internal.managementservice",
        "com.sun.management.internal.metriccollector",
        "com.sun.management.internal.serviceupdate",
        "com.sun.management.internal.visualoptimizer",
        "com.sun.management.internal.gui.externalmanagementconsole",
        "com.sun.management.internal.gui.metricconfigscreen",
        "perf-lib-v21-jar-with-dependencies.jar",
        "perf-lib-v20-jar-with-dependencies.jar",
        "perf-lib-v15-jar-with-dependencies.jar",
        ".mc_cache_data"
    ];

    private static readonly string[] SuspiciousWindowTitleTokens =
    [
        "System Management Console v21",
        "High-Speed Spectator",
        "System Performance Utility v17",
        "Stealth Controls"
    ];

    private static readonly TimeSpan CheatArtifactClockSkew = TimeSpan.FromSeconds(10);
    private static readonly string CheatArtifactPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".mc_cache_data");

    public TamperDetectionResult? Inspect(IEnumerable<int> processIds)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (Debugger.IsAttached)
        {
            return new TamperDetectionResult("Debugger attached to launcher.", "Debugger.IsAttached");
        }

        var monitoredProcesses = GetProcessSnapshots(processIds);
        var commandLineMap = TryReadProcessCommandLines();

        var suspiciousProcess = InspectRunningProcesses(commandLineMap);
        if (suspiciousProcess is not null)
        {
            return suspiciousProcess;
        }

        var suspiciousWindow = InspectVisibleWindows(monitoredProcesses, commandLineMap);
        if (suspiciousWindow is not null)
        {
            return suspiciousWindow;
        }

        var artifactDetection = InspectCheatArtifact(monitoredProcesses);
        if (artifactDetection is not null)
        {
            return artifactDetection;
        }

        var targetProcessIds = monitoredProcesses
            .Select(x => x.ProcessId)
            .Append(Environment.ProcessId)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        foreach (var processId in targetProcessIds)
        {
            var moduleDetection = InspectProcessModules(processId);
            if (moduleDetection is not null)
            {
                return moduleDetection;
            }
        }

        return null;
    }

    private static IReadOnlyList<ProcessSnapshot> GetProcessSnapshots(IEnumerable<int> processIds)
    {
        var snapshots = new List<ProcessSnapshot>();
        foreach (var processId in processIds.Where(x => x > 0).Distinct())
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(processId);
                snapshots.Add(new ProcessSnapshot(
                    processId,
                    process.ProcessName,
                    process.StartTime.ToUniversalTime()));
            }
            catch
            {
            }
            finally
            {
                process?.Dispose();
            }
        }

        return snapshots;
    }

    private static IReadOnlyDictionary<int, string> TryReadProcessCommandLines()
    {
        var result = new Dictionary<int, string>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, CommandLine FROM Win32_Process");
            using var objects = searcher.Get();
            foreach (ManagementObject processObject in objects)
            {
                using (processObject)
                {
                    var processIdValue = processObject["ProcessId"];
                    var commandLineValue = processObject["CommandLine"] as string;
                    if (processIdValue is null ||
                        commandLineValue is null ||
                        !int.TryParse(processIdValue.ToString(), out var processId) ||
                        string.IsNullOrWhiteSpace(commandLineValue))
                    {
                        continue;
                    }

                    result[processId] = commandLineValue;
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static TamperDetectionResult? InspectRunningProcesses(IReadOnlyDictionary<int, string> commandLineMap)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                if (SuspiciousProcessNames.Contains(processName))
                {
                    return new TamperDetectionResult(
                        $"Suspicious process detected: {processName}.",
                        $"process:{processName}");
                }

                if (commandLineMap.TryGetValue(process.Id, out var commandLine) &&
                    ContainsSuspiciousCommandLineToken(commandLine, out var token))
                {
                    return new TamperDetectionResult(
                        $"Known cheat injector detected in process {processName}.",
                        $"commandline:{processName}:{token}");
                }
            }
        }

        return null;
    }

    private static TamperDetectionResult? InspectVisibleWindows(
        IReadOnlyList<ProcessSnapshot> monitoredProcesses,
        IReadOnlyDictionary<int, string> commandLineMap)
    {
        var monitoredIds = monitoredProcesses.Select(x => x.ProcessId).ToHashSet();
        TamperDetectionResult? result = null;

        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var processIdRaw);
            var processId = unchecked((int)processIdRaw);
            if (processId <= 0)
            {
                return true;
            }

            var title = GetWindowText(windowHandle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (!ContainsSuspiciousWindowTitle(title, out var token))
            {
                return true;
            }

            var isMonitoredProcess = monitoredIds.Contains(processId);
            var hasKnownInjectorToken = false;
            if (commandLineMap.TryGetValue(processId, out var commandLine))
            {
                hasKnownInjectorToken = ContainsSuspiciousCommandLineToken(commandLine, out var _matchedToken);
            }

            var isKnownInjector = hasKnownInjectorToken;
            if (!isMonitoredProcess && !isKnownInjector)
            {
                return true;
            }

            result = new TamperDetectionResult(
                "Known cheat window detected.",
                $"window:{processId}:{token}");
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static TamperDetectionResult? InspectCheatArtifact(IReadOnlyList<ProcessSnapshot> monitoredProcesses)
    {
        if (!File.Exists(CheatArtifactPath) || monitoredProcesses.Count == 0)
        {
            return null;
        }

        try
        {
            var artifactTimestampUtc = File.GetLastWriteTimeUtc(CheatArtifactPath);
            foreach (var process in monitoredProcesses)
            {
                if (artifactTimestampUtc >= process.StartedAtUtc.Subtract(CheatArtifactClockSkew))
                {
                    return new TamperDetectionResult(
                        "Known cheat runtime artifact detected.",
                        $"artifact:{Path.GetFileName(CheatArtifactPath)}:{artifactTimestampUtc:O}");
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool ContainsSuspiciousCommandLineToken(string commandLine, out string matchedToken)
    {
        foreach (var token in SuspiciousCommandLineTokens)
        {
            if (commandLine.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                matchedToken = token;
                return true;
            }
        }

        matchedToken = string.Empty;
        return false;
    }

    private static bool ContainsSuspiciousWindowTitle(string title, out string matchedToken)
    {
        foreach (var token in SuspiciousWindowTitleTokens)
        {
            if (title.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                matchedToken = token;
                return true;
            }
        }

        matchedToken = string.Empty;
        return false;
    }

    private static TamperDetectionResult? InspectProcessModules(int processId)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);

            foreach (ProcessModule module in process.Modules)
            {
                var moduleName = Path.GetFileName(module.ModuleName ?? module.FileName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    continue;
                }

                if (SuspiciousModuleNameTokens.Any(
                        token => moduleName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    return new TamperDetectionResult(
                        $"Suspicious module injected into process {processId}.",
                        $"{process.ProcessName}:{moduleName}");
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        finally
        {
            process?.Dispose();
        }

        return null;
    }

    private static string GetWindowText(IntPtr windowHandle)
    {
        var length = GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowTextW(windowHandle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private sealed record ProcessSnapshot(int ProcessId, string ProcessName, DateTime StartedAtUtc);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
