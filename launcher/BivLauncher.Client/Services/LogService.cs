using System.Collections.Concurrent;

namespace BivLauncher.Client.Services;

public sealed class LogService(ISettingsService settingsService) : ILogService
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly object _fileLock = new();
    private const int MaxBufferedLines = 5000;

    public event Action<string>? LineAdded;

    public void LogInfo(string message)
    {
        Write("INFO", message);
    }

    public void LogError(string message)
    {
        Write("ERROR", message);
    }

    public IReadOnlyList<string> GetRecentLines(int count)
    {
        var snapshot = _lines.ToArray();
        if (snapshot.Length <= count)
        {
            return snapshot;
        }

        return snapshot[^count..];
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        _lines.Enqueue(line);
        while (_lines.Count > MaxBufferedLines && _lines.TryDequeue(out _))
        {
        }

        var logsDirectory = settingsService.GetLogsDirectory();
        Directory.CreateDirectory(logsDirectory);
        var logsFilePath = settingsService.GetLogsFilePath();

        lock (_fileLock)
        {
            File.AppendAllText(logsFilePath, line + Environment.NewLine);
        }

        LineAdded?.Invoke(line);
    }
}
