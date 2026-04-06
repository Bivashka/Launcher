using System.ComponentModel;
using System.Diagnostics;

namespace BivLauncher.Client.Services;

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

        var suspiciousProcess = InspectRunningProcesses();
        if (suspiciousProcess is not null)
        {
            return suspiciousProcess;
        }

        var targetProcessIds = processIds
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

    private static TamperDetectionResult? InspectRunningProcesses()
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
            }
        }

        return null;
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
}
