namespace BivLauncher.Api.Contracts.Public;

public sealed record BootstrapResponse(
    string PublicBaseUrl,
    BrandingConfig Branding,
    LauncherConstraints Constraints,
    IReadOnlyList<BootstrapProfileDto> Profiles,
    IReadOnlyList<BootstrapNewsItemDto> News,
    LauncherUpdateInfo? LauncherUpdate);

public sealed record BrandingConfig(
    string ProductName,
    string DeveloperName,
    string Tagline,
    string SupportUrl,
    string PrimaryColor,
    string AccentColor,
    string LogoText,
    string BackgroundImageUrl,
    double BackgroundOverlayOpacity,
    string LoginCardPosition,
    int LoginCardWidth);

public sealed record LauncherConstraints(
    bool ManagedLauncher,
    int MinRamMb,
    int ReservedSystemRamMb,
    bool InstallTelemetryEnabled,
    bool DiscordRpcEnabled,
    bool DiscordRpcPrivacyMode);

public sealed record BootstrapProfileDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    string IconKey,
    string IconUrl,
    int Priority,
    int RecommendedRamMb,
    string BundledRuntimeKey,
    PublicDiscordRpcConfig? DiscordRpc,
    IReadOnlyList<BootstrapServerDto> Servers);

public sealed record BootstrapServerDto(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string MainJarPath,
    string RuProxyAddress,
    int RuProxyPort,
    string RuJarPath,
    string IconKey,
    string IconUrl,
    string LoaderType,
    string McVersion,
    string BuildId,
    PublicDiscordRpcConfig? DiscordRpc,
    int Order);

public sealed record BootstrapNewsItemDto(
    Guid Id,
    string Title,
    string Body,
    string Source,
    bool Pinned,
    DateTime CreatedAtUtc);

public sealed record PublicDiscordRpcConfig(
    bool Enabled,
    string AppId,
    string DetailsText,
    string StateText,
    string LargeImageKey,
    string LargeImageText,
    string SmallImageKey,
    string SmallImageText);

public sealed record LauncherUpdateInfo(
    string LatestVersion,
    string DownloadUrl,
    string ReleaseNotes);
