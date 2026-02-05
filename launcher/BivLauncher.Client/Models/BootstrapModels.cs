using Avalonia.Media;

namespace BivLauncher.Client.Models;

public sealed class BootstrapResponse
{
    public string PublicBaseUrl { get; set; } = string.Empty;
    public BrandingConfig Branding { get; set; } = new();
    public LauncherConstraints Constraints { get; set; } = new();
    public List<BootstrapProfile> Profiles { get; set; } = [];
    public List<BootstrapNewsItem> News { get; set; } = [];
    public LauncherUpdateInfo? LauncherUpdate { get; set; }
}

public sealed class BrandingConfig
{
    public string ProductName { get; set; } = "BivLauncher";
    public string DeveloperName { get; set; } = "Bivashka";
    public string Tagline { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public string LogoText { get; set; } = "BLP";
    public string BackgroundImageUrl { get; set; } = string.Empty;
    public double BackgroundOverlayOpacity { get; set; } = 0.55;
    public string LoginCardPosition { get; set; } = "center";
    public int LoginCardWidth { get; set; } = 460;
}

public sealed class LauncherConstraints
{
    public bool ManagedLauncher { get; set; } = true;
    public int MinRamMb { get; set; } = 1024;
    public int ReservedSystemRamMb { get; set; } = 1024;
    public bool InstallTelemetryEnabled { get; set; } = true;
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool DiscordRpcPrivacyMode { get; set; }
}

public sealed class LauncherUpdateInfo
{
    public string LatestVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
}

public sealed class BootstrapProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public int RecommendedRamMb { get; set; } = 2048;
    public string BundledRuntimeKey { get; set; } = string.Empty;
    public DiscordRpcConfig? DiscordRpc { get; set; }
    public List<BootstrapServer> Servers { get; set; } = [];
}

public sealed class BootstrapServer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 25565;
    public string MainJarPath { get; set; } = "minecraft_main.jar";
    public string RuProxyAddress { get; set; } = string.Empty;
    public int RuProxyPort { get; set; } = 25565;
    public string RuJarPath { get; set; } = "minecraft_ru.jar";
    public string IconKey { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string LoaderType { get; set; } = "vanilla";
    public string McVersion { get; set; } = "1.21.1";
    public string BuildId { get; set; } = string.Empty;
    public DiscordRpcConfig? DiscordRpc { get; set; }
    public int Order { get; set; } = 100;
}

public sealed class DiscordRpcConfig
{
    public bool Enabled { get; set; } = true;
    public string AppId { get; set; } = string.Empty;
    public string DetailsText { get; set; } = string.Empty;
    public string StateText { get; set; } = string.Empty;
    public string LargeImageKey { get; set; } = string.Empty;
    public string LargeImageText { get; set; } = string.Empty;
    public string SmallImageKey { get; set; } = string.Empty;
    public string SmallImageText { get; set; } = string.Empty;
}

public sealed class BootstrapNewsItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool Pinned { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class LauncherNewsItem
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Preview { get; init; }
    public required string Meta { get; init; }
    public required string Source { get; init; }
    public required bool Pinned { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

public sealed class ManagedServerItem
{
    public required Guid ServerId { get; init; }
    public required string ProfileSlug { get; init; }
    public required string ProfileName { get; init; }
    public required string ServerName { get; init; }
    public required string Address { get; init; }
    public required int Port { get; init; }
    public required string LoaderType { get; init; }
    public required string McVersion { get; init; }
    public required int RecommendedRamMb { get; init; }
    public required string DiscordRpcAppId { get; init; }
    public required string DiscordRpcDetails { get; init; }
    public required string DiscordRpcState { get; init; }
    public required string DiscordRpcLargeImageKey { get; init; }
    public required string DiscordRpcLargeImageText { get; init; }
    public required string DiscordRpcSmallImageKey { get; init; }
    public required string DiscordRpcSmallImageText { get; init; }
    public required bool DiscordRpcEnabled { get; init; }
    public required string DiscordPreview { get; init; }
    public required IImage? Icon { get; init; }
    public required string MainAddress { get; init; }
    public required int MainPort { get; init; }
    public required string MainJarPath { get; init; }
    public required string RuProxyAddress { get; init; }
    public required int RuProxyPort { get; init; }
    public required string RuJarPath { get; init; }

    public string DisplayName => $"{ProfileName} / {ServerName}";
    public string AddressDisplay => $"{Address}:{Port}";
}
