namespace BivLauncher.Api.Services;

public interface IBuildSourcesLayoutService
{
    void EnsureProfileLayout(string profileSlug, string? previousProfileSlug = null);
    void EnsureServerLayout(string profileSlug, Guid serverId, string serverName);
}
