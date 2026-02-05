using BivLauncher.Client.Models;
using DiscordRPC;

namespace BivLauncher.Client.Services;

public sealed class DiscordRpcService(ILogService logService) : IDiscordRpcService, IDisposable
{
    private readonly object _syncRoot = new();
    private DiscordRpcClient? _client;
    private string _currentAppId = string.Empty;
    private DateTime _sessionStartedUtc = DateTime.UtcNow;
    private bool _globallyEnabled = true;
    private bool _privacyMode;
    private string _productName = "BivLauncher";

    public void ConfigurePolicy(bool enabled, bool privacyMode, string productName)
    {
        lock (_syncRoot)
        {
            _globallyEnabled = enabled;
            _privacyMode = privacyMode;
            _productName = string.IsNullOrWhiteSpace(productName) ? "BivLauncher" : productName.Trim();

            if (!_globallyEnabled)
            {
                DisposeClient();
            }
        }
    }

    public void UpdateIdlePresence(ManagedServerItem? server)
    {
        ApplyPresence(server, "In launcher");
    }

    public void SetLaunchingPresence(ManagedServerItem? server)
    {
        ApplyPresence(server, "Launching game");
    }

    public void SetInGamePresence(ManagedServerItem? server)
    {
        ApplyPresence(server, "In game");
    }

    public void ClearPresence()
    {
        lock (_syncRoot)
        {
            if (_client is null)
            {
                return;
            }

            try
            {
                _client.ClearPresence();
            }
            catch (Exception ex)
            {
                logService.LogError($"Discord RPC clear failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            DisposeClient();
        }
    }

    private void ApplyPresence(ManagedServerItem? server, string fallbackState)
    {
        lock (_syncRoot)
        {
            if (!_globallyEnabled)
            {
                DisposeClient();
                return;
            }

            if (!TryEnsureClient(server))
            {
                return;
            }

            if (_client is null)
            {
                return;
            }

            try
            {
                var details = ResolveDetails(server!, fallbackState);
                var state = ResolveState(server!, fallbackState);

                var presence = new RichPresence
                {
                    Details = details,
                    State = state,
                    Timestamps = new Timestamps(_sessionStartedUtc),
                    Assets = _privacyMode ? null : BuildAssets(server!)
                };

                _client.SetPresence(presence);
            }
            catch (Exception ex)
            {
                logService.LogError($"Discord RPC set presence failed: {ex.Message}");
            }
        }
    }

    private bool TryEnsureClient(ManagedServerItem? server)
    {
        if (!_globallyEnabled || server is null || !server.DiscordRpcEnabled || string.IsNullOrWhiteSpace(server.DiscordRpcAppId))
        {
            DisposeClient();
            return false;
        }

        var appId = server.DiscordRpcAppId.Trim();
        if (_client is not null && string.Equals(_currentAppId, appId, StringComparison.Ordinal))
        {
            return true;
        }

        DisposeClient();
        try
        {
            var client = new DiscordRpcClient(appId);
            if (!client.Initialize())
            {
                client.Dispose();
                logService.LogError($"Discord RPC initialize failed for appId {appId}.");
                return false;
            }

            _client = client;
            _currentAppId = appId;
            _sessionStartedUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            logService.LogError($"Discord RPC startup failed: {ex.Message}");
            DisposeClient();
            return false;
        }
    }

    private string ResolveDetails(ManagedServerItem server, string fallbackState)
    {
        if (_privacyMode)
        {
            return _productName;
        }

        if (!string.IsNullOrWhiteSpace(server.DiscordRpcDetails))
        {
            return server.DiscordRpcDetails.Trim();
        }

        return server.DisplayName;
    }

    private string ResolveState(ManagedServerItem server, string fallbackState)
    {
        if (_privacyMode)
        {
            return fallbackState;
        }

        if (!string.IsNullOrWhiteSpace(server.DiscordRpcState))
        {
            return server.DiscordRpcState.Trim();
        }

        return fallbackState;
    }

    private static Assets? BuildAssets(ManagedServerItem server)
    {
        var hasLarge = !string.IsNullOrWhiteSpace(server.DiscordRpcLargeImageKey);
        var hasSmall = !string.IsNullOrWhiteSpace(server.DiscordRpcSmallImageKey);
        if (!hasLarge && !hasSmall)
        {
            return null;
        }

        return new Assets
        {
            LargeImageKey = hasLarge ? server.DiscordRpcLargeImageKey.Trim() : null,
            LargeImageText = string.IsNullOrWhiteSpace(server.DiscordRpcLargeImageText) ? null : server.DiscordRpcLargeImageText.Trim(),
            SmallImageKey = hasSmall ? server.DiscordRpcSmallImageKey.Trim() : null,
            SmallImageText = string.IsNullOrWhiteSpace(server.DiscordRpcSmallImageText) ? null : server.DiscordRpcSmallImageText.Trim()
        };
    }

    private void DisposeClient()
    {
        if (_client is null)
        {
            _currentAppId = string.Empty;
            return;
        }

        try
        {
            _client.ClearPresence();
        }
        catch
        {
        }

        try
        {
            _client.Dispose();
        }
        catch
        {
        }

        _client = null;
        _currentAppId = string.Empty;
    }
}
