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
    private const int HookProbeByteCount = 16;
    private const int MinimumSuspiciousTimerHookCount = 2;
    private const int SystemExtendedHandleInformationClass = 64;
    private const int NtStatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private static readonly object ExportProbeCacheSyncRoot = new();
    private static readonly HashSet<string> SuspiciousProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cheatengine",
        "cheatengine-x86_64",
        "cheatengine-i386",
        "ceserver",
        "dbk32",
        "dbk64",
        "vehdebug-x86_64",
        "vehdebug-i386",
        "luaclient",
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
        "speedhack-x86_64",
        "speedhack-i386",
        "ceserver",
        "dbk32",
        "dbk64",
        "vehdebug",
        "luaclient",
        "lua53",
        "lua54",
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
    private static readonly TimeApiHookProbe[] SuspiciousTimerHookProbes =
    [
        new("kernelbase.dll", "QueryPerformanceCounter"),
        new("kernelbase.dll", "GetTickCount"),
        new("kernelbase.dll", "GetTickCount64"),
        new("kernelbase.dll", "GetSystemTimeAsFileTime"),
        new("winmm.dll", "timeGetTime"),
        new("ntdll.dll", "NtQueryPerformanceCounter"),
        new("ntdll.dll", "NtQuerySystemTime")
    ];
    private static readonly Dictionary<string, TimerExportProbe> TimerExportProbeCache = new(StringComparer.OrdinalIgnoreCase);

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

        var timerHookDetection = InspectTimerApiHooks(monitoredProcesses);
        if (timerHookDetection is not null)
        {
            return timerHookDetection;
        }

        var externalHandleDetection = InspectExternalProcessHandles(monitoredProcesses);
        if (externalHandleDetection is not null)
        {
            return externalHandleDetection;
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

    private static TamperDetectionResult? InspectTimerApiHooks(IReadOnlyList<ProcessSnapshot> monitoredProcesses)
    {
        foreach (var process in monitoredProcesses)
        {
            var detection = InspectTimerApiHooks(process.ProcessId);
            if (detection is not null)
            {
                return detection;
            }
        }

        return null;
    }

    private static TamperDetectionResult? InspectExternalProcessHandles(IReadOnlyList<ProcessSnapshot> monitoredProcesses)
    {
        if (monitoredProcesses.Count == 0 ||
            !TryQuerySystemHandles(out var handles))
        {
            return null;
        }

        var currentProcessId = Environment.ProcessId;
        foreach (var process in monitoredProcesses)
        {
            if (!TryResolveProcessObjectAddress(process.ProcessId, currentProcessId, handles, out var objectAddress))
            {
                continue;
            }

            foreach (var handle in handles)
            {
                if (handle.ObjectAddress != objectAddress ||
                    handle.OwnerProcessId == currentProcessId ||
                    handle.OwnerProcessId == process.ProcessId ||
                    handle.OwnerProcessId <= 0 ||
                    !HasDangerousExternalProcessAccess(handle.GrantedAccess))
                {
                    continue;
                }

                var ownerProcessName = TryGetProcessName(handle.OwnerProcessId);
                return new TamperDetectionResult(
                    $"External process opened a dangerous handle to monitored process {process.ProcessId}.",
                    $"handle:{ownerProcessName}:{handle.OwnerProcessId}:0x{handle.GrantedAccess:X8}->{process.ProcessName}:{process.ProcessId}");
            }
        }

        return null;
    }

    private static TamperDetectionResult? InspectTimerApiHooks(int processId)
    {
        Process? process = null;
        IntPtr processHandle = IntPtr.Zero;
        try
        {
            process = Process.GetProcessById(processId);
            var remoteModules = GetRemoteModuleSnapshots(process);
            if (remoteModules.Count == 0)
            {
                return null;
            }

            processHandle = OpenProcess(
                ProcessAccessRights.QueryInformation | ProcessAccessRights.VirtualMemoryRead,
                false,
                processId);
            if (processHandle == IntPtr.Zero)
            {
                return null;
            }

            if (ShouldSkipTimerHookInspection(processHandle))
            {
                return null;
            }

            List<string>? suspiciousHooks = null;
            foreach (var probe in SuspiciousTimerHookProbes)
            {
                if (!remoteModules.TryGetValue(probe.ModuleName, out var remoteModule) ||
                    !TryResolveTimerExportProbe(probe.ModuleName, probe.ExportName, out var exportProbe))
                {
                    continue;
                }

                var remoteAddress = new IntPtr(remoteModule.BaseAddress.ToInt64() + exportProbe.Offset);
                if (!TryReadProcessBytes(processHandle, remoteAddress, HookProbeByteCount, out var remoteBytes) ||
                    remoteBytes.SequenceEqual(exportProbe.BaselineBytes) ||
                    !LooksLikeHookStub(remoteBytes))
                {
                    continue;
                }

                suspiciousHooks ??= [];
                suspiciousHooks.Add($"{probe.ModuleName}!{probe.ExportName}");
                if (suspiciousHooks.Count >= MinimumSuspiciousTimerHookCount)
                {
                    return new TamperDetectionResult(
                        $"Timer API hook detected in process {processId}.",
                        $"timerhook:{process.ProcessName}:{string.Join(",", suspiciousHooks)}");
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
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }

            process?.Dispose();
        }

        return null;
    }

    private static bool TryQuerySystemHandles(out List<SystemHandleSnapshot> handles)
    {
        handles = [];
        var bufferLength = 0x10000;
        var buffer = IntPtr.Zero;
        try
        {
            while (true)
            {
                buffer = Marshal.AllocHGlobal(bufferLength);
                var ntstatus = NtQuerySystemInformation(
                    SystemExtendedHandleInformationClass,
                    buffer,
                    bufferLength,
                    out var returnLength);
                if (ntstatus == 0)
                {
                    break;
                }

                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                if (ntstatus != NtStatusInfoLengthMismatch)
                {
                    return false;
                }

                bufferLength = Math.Max(bufferLength * 2, returnLength.ToInt32() + 4096);
            }

            var handleCount = Marshal.ReadIntPtr(buffer);
            var count = checked((int)handleCount.ToInt64());
            var entryOffset = IntPtr.Size * 2;
            var entrySize = Marshal.SizeOf<SystemHandleTableEntryInfoEx>();
            for (var index = 0; index < count; index++)
            {
                var entryPtr = IntPtr.Add(buffer, entryOffset + (index * entrySize));
                var entry = Marshal.PtrToStructure<SystemHandleTableEntryInfoEx>(entryPtr);
                var ownerProcessId = unchecked((int)entry.UniqueProcessId.ToInt64());
                if (ownerProcessId <= 0 || entry.Object == IntPtr.Zero)
                {
                    continue;
                }

                handles.Add(new SystemHandleSnapshot(
                    ownerProcessId,
                    entry.HandleValue,
                    entry.Object,
                    entry.GrantedAccess));
            }

            return true;
        }
        catch
        {
            handles.Clear();
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static bool TryResolveProcessObjectAddress(
        int targetProcessId,
        int currentProcessId,
        IReadOnlyList<SystemHandleSnapshot> handles,
        out IntPtr objectAddress)
    {
        objectAddress = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        try
        {
            processHandle = OpenProcess(
                ProcessAccessRights.QueryInformation,
                false,
                targetProcessId);
            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            var rawHandleValue = processHandle.ToInt64();
            foreach (var handle in handles)
            {
                if (handle.OwnerProcessId == currentProcessId &&
                    handle.HandleValue.ToInt64() == rawHandleValue)
                {
                    objectAddress = handle.ObjectAddress;
                    return objectAddress != IntPtr.Zero;
                }
            }

            return false;
        }
        finally
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }

    private static bool HasDangerousExternalProcessAccess(uint grantedAccess)
    {
        const uint dangerousMask =
            (uint)ProcessAccessRights.CreateThread |
            (uint)ProcessAccessRights.VirtualMemoryOperation |
            (uint)ProcessAccessRights.VirtualMemoryWrite |
            (uint)ProcessAccessRights.DuplicateHandle |
            (uint)ProcessAccessRights.SetInformation |
            (uint)ProcessAccessRights.SuspendResume;

        return (grantedAccess & dangerousMask) != 0;
    }

    private static string TryGetProcessName(int processId)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            return string.IsNullOrWhiteSpace(process.ProcessName)
                ? $"pid-{processId}"
                : process.ProcessName;
        }
        catch
        {
            return $"pid-{processId}";
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static bool TryResolveTimerExportProbe(string moduleName, string exportName, out TimerExportProbe probe)
    {
        var cacheKey = $"{moduleName}!{exportName}";
        lock (ExportProbeCacheSyncRoot)
        {
            if (TimerExportProbeCache.TryGetValue(cacheKey, out probe))
            {
                return true;
            }
        }

        var moduleHandle = GetModuleHandleW(moduleName);
        var loadedForProbe = false;
        if (moduleHandle == IntPtr.Zero)
        {
            moduleHandle = LoadLibraryW(moduleName);
            loadedForProbe = moduleHandle != IntPtr.Zero;
        }

        if (moduleHandle == IntPtr.Zero)
        {
            probe = default;
            return false;
        }

        try
        {
            var exportAddress = GetProcAddress(moduleHandle, exportName);
            if (exportAddress == IntPtr.Zero ||
                !TryGetLocalModuleRange(moduleName, out var localModuleBase, out var localModuleSize))
            {
                probe = default;
                return false;
            }

            var moduleStart = localModuleBase.ToInt64();
            var moduleEnd = moduleStart + localModuleSize;
            var exportStart = exportAddress.ToInt64();
            if (exportStart < moduleStart || exportStart + HookProbeByteCount > moduleEnd)
            {
                probe = default;
                return false;
            }

            var baselineBytes = new byte[HookProbeByteCount];
            Marshal.Copy(exportAddress, baselineBytes, 0, baselineBytes.Length);

            probe = new TimerExportProbe(
                moduleName,
                exportName,
                exportStart - moduleStart,
                baselineBytes);

            lock (ExportProbeCacheSyncRoot)
            {
                TimerExportProbeCache[cacheKey] = probe;
            }

            return true;
        }
        finally
        {
            if (loadedForProbe)
            {
                FreeLibrary(moduleHandle);
            }
        }
    }

    private static bool TryGetLocalModuleRange(string moduleName, out IntPtr moduleBase, out int moduleSize)
    {
        using var currentProcess = Process.GetCurrentProcess();
        foreach (ProcessModule module in currentProcess.Modules)
        {
            if (!string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            moduleBase = module.BaseAddress;
            moduleSize = module.ModuleMemorySize;
            return true;
        }

        moduleBase = IntPtr.Zero;
        moduleSize = 0;
        return false;
    }

    private static IReadOnlyDictionary<string, RemoteModuleSnapshot> GetRemoteModuleSnapshots(Process process)
    {
        var result = new Dictionary<string, RemoteModuleSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (ProcessModule module in process.Modules)
        {
            var moduleName = module.ModuleName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(moduleName) || result.ContainsKey(moduleName))
            {
                continue;
            }

            result[moduleName] = new RemoteModuleSnapshot(moduleName, module.BaseAddress);
        }

        return result;
    }

    private static bool ShouldSkipTimerHookInspection(IntPtr processHandle)
    {
        if (!Environment.Is64BitProcess)
        {
            return false;
        }

        return IsWow64Process(processHandle, out var isWow64) && isWow64;
    }

    private static bool TryReadProcessBytes(IntPtr processHandle, IntPtr address, int count, out byte[] buffer)
    {
        buffer = new byte[count];
        return ReadProcessMemory(processHandle, address, buffer, buffer.Length, out var bytesRead) &&
               bytesRead.ToInt64() == buffer.Length;
    }

    private static bool LooksLikeHookStub(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
        {
            return false;
        }

        if (bytes[0] is 0xE9 or 0xE8 or 0xEB or 0xEA)
        {
            return true;
        }

        if (bytes[0] == 0xFF && bytes[1] is 0x25 or 0x15)
        {
            return true;
        }

        if (bytes.Length >= 12 &&
            bytes[0] == 0x48 &&
            bytes[1] == 0xB8 &&
            bytes[10] == 0xFF &&
            bytes[11] == 0xE0)
        {
            return true;
        }

        if (bytes.Length >= 13 &&
            bytes[0] == 0x49 &&
            bytes[1] == 0xBB &&
            bytes[10] == 0x41 &&
            bytes[11] == 0xFF &&
            bytes[12] == 0xE3)
        {
            return true;
        }

        return bytes.Length >= 6 &&
               bytes[0] == 0x68 &&
               bytes[5] == 0xC3;
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

    private readonly record struct ProcessSnapshot(int ProcessId, string ProcessName, DateTime StartedAtUtc);
    private readonly record struct RemoteModuleSnapshot(string ModuleName, IntPtr BaseAddress);
    private readonly record struct SystemHandleSnapshot(
        int OwnerProcessId,
        IntPtr HandleValue,
        IntPtr ObjectAddress,
        uint GrantedAccess);
    private readonly record struct TimeApiHookProbe(string ModuleName, string ExportName);
    private readonly record struct TimerExportProbe(string ModuleName, string ExportName, long Offset, byte[] BaselineBytes);
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SystemHandleTableEntryInfoEx
    {
        public readonly IntPtr Object;
        public readonly IntPtr UniqueProcessId;
        public readonly IntPtr HandleValue;
        public readonly uint GrantedAccess;
        public readonly ushort CreatorBackTraceIndex;
        public readonly ushort ObjectTypeIndex;
        public readonly uint HandleAttributes;
        public readonly uint Reserved;
    }

    [Flags]
    private enum ProcessAccessRights : uint
    {
        CreateThread = 0x0002,
        DuplicateHandle = 0x0040,
        SetInformation = 0x0200,
        SuspendResume = 0x0800,
        VirtualMemoryOperation = 0x0008,
        VirtualMemoryRead = 0x0010,
        VirtualMemoryWrite = 0x0020,
        QueryInformation = 0x0400
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(ProcessAccessRights dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        int dwSize,
        out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out IntPtr returnLength);

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
