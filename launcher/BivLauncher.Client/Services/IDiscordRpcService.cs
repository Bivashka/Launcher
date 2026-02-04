using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface IDiscordRpcService
{
    void UpdateIdlePresence(ManagedServerItem? server);
    void SetLaunchingPresence(ManagedServerItem? server);
    void SetInGamePresence(ManagedServerItem? server);
    void ClearPresence();
}
