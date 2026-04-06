namespace BivLauncher.Client.Services;

public interface ILauncherTamperMonitor
{
    TamperDetectionResult? Inspect(IEnumerable<int> processIds);
}

public sealed record TamperDetectionResult(string Reason, string Evidence);
