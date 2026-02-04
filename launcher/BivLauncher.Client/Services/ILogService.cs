namespace BivLauncher.Client.Services;

public interface ILogService
{
    event Action<string>? LineAdded;
    void LogInfo(string message);
    void LogError(string message);
    IReadOnlyList<string> GetRecentLines(int count);
}
