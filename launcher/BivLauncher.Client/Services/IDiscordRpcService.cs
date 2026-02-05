using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface IDiscordRpcService
{
    void ConfigurePolicy(bool enabled, bool privacyMode, string productName);
    void UpdateIdlePresence(ManagedServerItem? server);
    void SetLaunchingPresence(ManagedServerItem? server);
    void SetInGamePresence(ManagedServerItem? server);
    void ClearPresence();
}
