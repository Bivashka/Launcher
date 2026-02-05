using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BivLauncher.Client.Models;
using BivLauncher.Client.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BivLauncher.Client.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILauncherApiService _launcherApiService;
    private readonly IManifestInstallerService _manifestInstallerService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IDiscordRpcService _discordRpcService;
    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly IPendingSubmissionService _pendingSubmissionService;
    private readonly ILogService _logService;
    private readonly HttpClient _iconHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Dictionary<string, IImage?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IImage?> _brandingImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _liveLogLines = new();
    private const int MaxLiveLogLines = 500;
    private readonly string _currentLauncherVersion = GetCurrentLauncherVersion();
    private string _languageCode = "ru";
    private readonly Dictionary<string, string> _profileRouteSelections = new(StringComparer.OrdinalIgnoreCase);
    private bool _isSyncingLanguageOption;
    private bool _isSyncingJavaModeOption;
    private bool _isSyncingRouteOption;
    private bool _installTelemetrySent;

    private LauncherSettings _settings = new();

    public MainWindowViewModel(
        ISettingsService settingsService,
        ILauncherApiService launcherApiService,
        IManifestInstallerService manifestInstallerService,
        IGameLaunchService gameLaunchService,
        IDiscordRpcService discordRpcService,
        ILauncherUpdateService launcherUpdateService,
        IPendingSubmissionService pendingSubmissionService,
        ILogService logService)
    {
        _settingsService = settingsService;
        _launcherApiService = launcherApiService;
        _manifestInstallerService = manifestInstallerService;
        _gameLaunchService = gameLaunchService;
        _discordRpcService = discordRpcService;
        _launcherUpdateService = launcherUpdateService;
        _pendingSubmissionService = pendingSubmissionService;
        _logService = logService;

        ManagedServers = new ObservableCollection<ManagedServerItem>();
        NewsItems = new ObservableCollection<LauncherNewsItem>();
        LanguageOptions = new ObservableCollection<LocalizedOption>(LauncherLocalization.SupportedLanguages);
        JavaModeOptions = new ObservableCollection<LocalizedOption>();
        RouteOptions = new ObservableCollection<LocalizedOption>();
        RebuildJavaModeOptions();
        RebuildRouteOptions();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanOperate);
        LoginCommand = new AsyncRelayCommand(LoginAsync, CanOperate);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanOperate);
        VerifyFilesCommand = new AsyncRelayCommand(VerifyFilesAsync, CanVerifyOrLaunch);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, CanVerifyOrLaunch);
        CopyCrashCommand = new AsyncRelayCommand(CopyCrashAsync);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        ToggleSettingsCommand = new RelayCommand(ToggleSettings, CanToggleSettings);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        OpenUpdateUrlCommand = new RelayCommand(OpenUpdateUrl, CanOpenUpdateUrl);
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, CanDownloadUpdate);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, CanInstallUpdate);

        _logService.LineAdded += OnLogLineAdded;
    }

    public ObservableCollection<ManagedServerItem> ManagedServers { get; }
    public ObservableCollection<LauncherNewsItem> NewsItems { get; }
    public ObservableCollection<LocalizedOption> LanguageOptions { get; }
    public ObservableCollection<LocalizedOption> JavaModeOptions { get; }
    public ObservableCollection<LocalizedOption> RouteOptions { get; }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand VerifyFilesCommand { get; }
    public IAsyncRelayCommand LaunchCommand { get; }
    public IAsyncRelayCommand CopyCrashCommand { get; }
    public IRelayCommand OpenLogsFolderCommand { get; }
    public IRelayCommand ToggleSettingsCommand { get; }
    public IRelayCommand CloseSettingsCommand { get; }
    public IRelayCommand OpenUpdateUrlCommand { get; }
    public IAsyncRelayCommand DownloadUpdateCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }

    [ObservableProperty]
    private string _productName = "BivLauncher";

    [ObservableProperty]
    private string _tagline = "Управляемый лаунчер";

    [ObservableProperty]
    private IBrush _launcherBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#07111E"), 0),
            new GradientStop(Color.Parse("#0C1C30"), 0.55),
            new GradientStop(Color.Parse("#0A1524"), 1)
        }
    };

    [ObservableProperty]
    private IBrush _heroBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#103357"), 0),
            new GradientStop(Color.Parse("#17416E"), 1)
        }
    };

    [ObservableProperty]
    private IBrush _heroBorderBrush = new SolidColorBrush(Color.Parse("#4A7098"));

    [ObservableProperty]
    private IBrush _loginCardBackgroundBrush = new SolidColorBrush(Color.Parse("#121F34"));

    [ObservableProperty]
    private IBrush _loginCardBorderBrush = new SolidColorBrush(Color.Parse("#2E557F"));

    [ObservableProperty]
    private IBrush _playButtonBackgroundBrush = new SolidColorBrush(Color.Parse("#1F8F63"));

    [ObservableProperty]
    private IBrush _playButtonBorderBrush = new SolidColorBrush(Color.Parse("#4EC598"));

    [ObservableProperty]
    private IImage? _brandingBackgroundImage;

    [ObservableProperty]
    private double _brandingBackgroundOverlayOpacity = 0.55;

    [ObservableProperty]
    private HorizontalAlignment _loginCardHorizontalAlignment = HorizontalAlignment.Center;

    [ObservableProperty]
    private double _loginCardWidth = 460;

    [ObservableProperty]
    private string _statusText = "Загрузка...";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _apiBaseUrl = "http://localhost:8080";

    [ObservableProperty]
    private string _installDirectory = string.Empty;

    [ObservableProperty]
    private bool _debugMode;

    [ObservableProperty]
    private int _ramMb = 2048;

    [ObservableProperty]
    private int _ramMinMb = 1024;

    [ObservableProperty]
    private int _ramMaxMb = 8192;

    [ObservableProperty]
    private string _javaMode = "Auto";

    [ObservableProperty]
    private LocalizedOption? _selectedLanguageOption;

    [ObservableProperty]
    private LocalizedOption? _selectedJavaModeOption;

    [ObservableProperty]
    private LocalizedOption? _selectedRouteOption;

    [ObservableProperty]
    private ManagedServerItem? _selectedServer;

    [ObservableProperty]
    private LauncherNewsItem? _selectedNewsItem;

    [ObservableProperty]
    private string _liveLogs = string.Empty;

    [ObservableProperty]
    private string _crashSummary = string.Empty;

    [ObservableProperty]
    private bool _hasCrash;

    [ObservableProperty]
    private string _playerUsername = string.Empty;

    [ObservableProperty]
    private string _playerPassword = string.Empty;

    [ObservableProperty]
    private string _playerTwoFactorCode = string.Empty;

    [ObservableProperty]
    private string _playerLoggedInAs = string.Empty;

    [ObservableProperty]
    private string _authStatusText = "Не выполнен вход.";

    [ObservableProperty]
    private bool _isPlayerLoggedIn;

    [ObservableProperty]
    private bool _isTwoFactorStepActive;

    [ObservableProperty]
    private string _twoFactorSetupSecret = string.Empty;

    [ObservableProperty]
    private string _twoFactorSetupUri = string.Empty;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _hasSkin;

    [ObservableProperty]
    private bool _hasCape;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _latestLauncherVersion = string.Empty;

    [ObservableProperty]
    private string _updateDownloadUrl = string.Empty;

    [ObservableProperty]
    private string _updateReleaseNotes = string.Empty;

    [ObservableProperty]
    private string _downloadedUpdatePackagePath = string.Empty;

    [ObservableProperty]
    private string _downloadedUpdateVersion = string.Empty;

    [ObservableProperty]
    private string _updateDownloadStatusText = string.Empty;

    [ObservableProperty]
    private bool _isUpdateDownloading;

    [ObservableProperty]
    private int _updateDownloadProgressPercent;

    public string RefreshButtonText => T("button.refresh");
    public string SaveSettingsButtonText => T("button.saveSettings");
    public string ServersHeaderText => T("header.servers");
    public string NewsHeaderText => T("header.news");
    public string SettingsHeaderText => T("header.settings");
    public string ApiBaseUrlLabelText => T("label.apiBaseUrl");
    public string InstallDirectoryLabelText => T("label.installDirectory");
    public string LanguageLabelText => T("label.language");
    public string AccountHeaderText => T("header.account");
    public string UsernameLabelText => T("label.username");
    public string PasswordLabelText => T("label.password");
    public string TwoFactorCodeLabelText => T("label.twoFactorCode");
    public string LoginButtonText => IsTwoFactorStepActive ? T("button.verifyTwoFactor") : T("button.login");
    public string TwoFactorHintText => BuildTwoFactorHintText();
    public string SkinStatusText => F("status.skin", BoolWord(HasSkin));
    public string CapeStatusText => F("status.cape", BoolWord(HasCape));
    public string RuntimeHeaderText => T("header.runtime");
    public string RouteLabelText => T("label.route");
    public string JavaModeLabelText => T("label.javaMode");
    public string RamLabelText => T("label.ram");
    public string RamMinText => F("status.ramMin", RamMinMb);
    public string RamMaxText => F("status.ramMax", RamMaxMb);
    public string DebugModeLabelText => T("label.debugMode");
    public string VerifyFilesButtonText => T("button.verifyFiles");
    public string PlayButtonText => T("button.play");
    public string OpenLogsFolderButtonText => T("button.openLogs");
    public string CrashSummaryHeaderText => T("header.crash");
    public string CopyCrashButtonText => T("button.copyCrash");
    public string CrashFlagText => F("status.hasCrash", BoolWord(HasCrash));
    public string DebugLogHeaderText => T("header.debugLog");
    public string SelectedNewsTitle => SelectedNewsItem?.Title ?? string.Empty;
    public string SelectedNewsBody => SelectedNewsItem?.Body ?? string.Empty;
    public string UpdateHeaderText => "Launcher update";
    public string CurrentVersionText => $"Current version: {_currentLauncherVersion}";
    public string LatestVersionText => string.IsNullOrWhiteSpace(LatestLauncherVersion)
        ? "Latest version: -"
        : $"Latest version: {LatestLauncherVersion}";
    public string UpdateAvailabilityText => IsUpdateAvailable
        ? "Update available."
        : "No update available.";
    public string OpenUpdateButtonText => "Open download page";
    public string DownloadUpdateButtonText => "Download update";
    public string InstallUpdateButtonText => "Install and restart";
    public string UpdateDownloadProgressText => IsUpdateDownloading
        ? $"Downloading... {UpdateDownloadProgressPercent}%"
        : UpdateDownloadStatusText;
    public bool HasUpdateReleaseNotes => !string.IsNullOrWhiteSpace(UpdateReleaseNotes);
    public bool IsUpdatePackageReady => !string.IsNullOrWhiteSpace(DownloadedUpdatePackagePath) && File.Exists(DownloadedUpdatePackagePath);
    public bool HasBrandingBackgroundImage => BrandingBackgroundImage is not null;
    public bool IsLoginRequired => !IsPlayerLoggedIn;
    public bool IsLauncherReady => IsPlayerLoggedIn;

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();
        _languageCode = LauncherLocalization.NormalizeLanguage(_settings.Language);
        SyncSelectedLanguageOption();
        RebuildJavaModeOptions();
        RebuildRouteOptions();
        RefreshLocalizedBindings();
        LoadRouteSelections(_settings.ProfileRouteSelections ?? []);

        ApiBaseUrl = NormalizeBaseUrl(_settings.ApiBaseUrl);
        InstallDirectory = string.IsNullOrWhiteSpace(_settings.InstallDirectory)
            ? _settingsService.GetDefaultInstallDirectory()
            : _settings.InstallDirectory;
        DebugMode = _settings.DebugMode;
        RamMb = _settings.RamMb;
        JavaMode = NormalizeJavaMode(_settings.JavaMode);
        SyncSelectedJavaModeOption();
        PlayerUsername = _settings.LastPlayerUsername;
        PlayerTwoFactorCode = string.Empty;
        IsTwoFactorStepActive = false;
        TwoFactorSetupSecret = string.Empty;
        TwoFactorSetupUri = string.Empty;
        AuthStatusText = T("status.notLoggedIn");

        await RefreshAsync();
        StatusText = T("status.ready");
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        LoginCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
        ToggleSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedServerChanged(ManagedServerItem? value)
    {
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        SyncSelectedRouteOption();

        if (value is null)
        {
            _discordRpcService.ClearPresence();
            return;
        }

        _discordRpcService.UpdateIdlePresence(value);

        var recommended = Math.Clamp(value.RecommendedRamMb, RamMinMb, RamMaxMb);
        if (RamMb < RamMinMb || RamMb > RamMaxMb)
        {
            RamMb = recommended;
        }
    }

    partial void OnRamMbChanged(int value)
    {
        if (value < RamMinMb)
        {
            RamMb = RamMinMb;
        }
        else if (value > RamMaxMb)
        {
            RamMb = RamMaxMb;
        }
    }

    partial void OnDebugModeChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        var recent = _logService.GetRecentLines(120);
        LiveLogs = string.Join(Environment.NewLine, recent);
    }

    partial void OnRamMinMbChanged(int value)
    {
        OnPropertyChanged(nameof(RamMinText));
    }

    partial void OnRamMaxMbChanged(int value)
    {
        OnPropertyChanged(nameof(RamMaxText));
    }

    partial void OnHasSkinChanged(bool value)
    {
        OnPropertyChanged(nameof(SkinStatusText));
    }

    partial void OnHasCapeChanged(bool value)
    {
        OnPropertyChanged(nameof(CapeStatusText));
    }

    partial void OnHasCrashChanged(bool value)
    {
        OnPropertyChanged(nameof(CrashFlagText));
    }

    partial void OnSelectedNewsItemChanged(LauncherNewsItem? value)
    {
        OnPropertyChanged(nameof(SelectedNewsTitle));
        OnPropertyChanged(nameof(SelectedNewsBody));
    }

    partial void OnIsTwoFactorStepActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(TwoFactorHintText));
    }

    partial void OnTwoFactorSetupSecretChanged(string value)
    {
        OnPropertyChanged(nameof(TwoFactorHintText));
    }

    partial void OnTwoFactorSetupUriChanged(string value)
    {
        OnPropertyChanged(nameof(TwoFactorHintText));
    }

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateAvailabilityText));
        OpenUpdateUrlCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnLatestLauncherVersionChanged(string value)
    {
        OnPropertyChanged(nameof(LatestVersionText));
    }

    partial void OnUpdateDownloadUrlChanged(string value)
    {
        OpenUpdateUrlCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdateReleaseNotesChanged(string value)
    {
        OnPropertyChanged(nameof(HasUpdateReleaseNotes));
    }

    partial void OnDownloadedUpdatePackagePathChanged(string value)
    {
        OnPropertyChanged(nameof(IsUpdatePackageReady));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUpdateDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateDownloadProgressText));
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdateDownloadStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(UpdateDownloadProgressText));
    }

    partial void OnUpdateDownloadProgressPercentChanged(int value)
    {
        OnPropertyChanged(nameof(UpdateDownloadProgressText));
    }

    partial void OnBrandingBackgroundImageChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasBrandingBackgroundImage));
    }

    partial void OnJavaModeChanged(string value)
    {
        JavaMode = NormalizeJavaMode(value);
        SyncSelectedJavaModeOption();
    }

    partial void OnSelectedLanguageOptionChanged(LocalizedOption? value)
    {
        if (_isSyncingLanguageOption || value is null)
        {
            return;
        }

        _languageCode = LauncherLocalization.NormalizeLanguage(value.Value);
        RebuildJavaModeOptions();
        RebuildRouteOptions();
        RefreshLocalizedBindings();
        RelocalizeCollections();

        if (!IsPlayerLoggedIn)
        {
            AuthStatusText = T("status.notLoggedIn");
        }
    }

    partial void OnSelectedJavaModeOptionChanged(LocalizedOption? value)
    {
        if (_isSyncingJavaModeOption || value is null)
        {
            return;
        }

        JavaMode = NormalizeJavaMode(value.Value);
    }

    partial void OnSelectedRouteOptionChanged(LocalizedOption? value)
    {
        if (_isSyncingRouteOption || value is null || SelectedServer is null)
        {
            return;
        }

        var normalized = NormalizeRouteCode(value.Value);
        _profileRouteSelections[SelectedServer.ProfileSlug] = normalized;
        SyncSelectedRouteOption();
    }

    private bool CanOperate() => !IsBusy;

    private bool CanVerifyOrLaunch() => !IsBusy && IsPlayerLoggedIn && SelectedServer is not null;

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            StatusText = T("status.fetchingBootstrap");
            var bootstrap = await _launcherApiService.GetBootstrapAsync(ApiBaseUrl);
            await FlushPendingSubmissionsAsync();
            await TrySubmitInstallTelemetryAsync(bootstrap);

            await ApplyBrandingAsync(ApiBaseUrl, bootstrap.Branding);
            var discordRpcEnabled = bootstrap.Constraints.DiscordRpcEnabled;
            var discordRpcPrivacyMode = bootstrap.Constraints.DiscordRpcPrivacyMode;
            _discordRpcService.ConfigurePolicy(discordRpcEnabled, discordRpcPrivacyMode, ProductName);
            ApplyLauncherUpdateInfo(bootstrap.LauncherUpdate);

            var totalMemoryMb = GetTotalAvailableMemoryMb();
            RamMinMb = Math.Max(bootstrap.Constraints.MinRamMb, 512);
            RamMaxMb = Math.Max(RamMinMb, totalMemoryMb - Math.Max(bootstrap.Constraints.ReservedSystemRamMb, 512));
            RamMb = Math.Clamp(RamMb, RamMinMb, RamMaxMb);

            var allServers = new List<ManagedServerItem>();
            var orderedProfiles = bootstrap.Profiles.OrderBy(profile => profile.Priority);
            foreach (var profile in orderedProfiles)
            {
                var orderedServers = profile.Servers.OrderBy(server => server.Order);
                foreach (var server in orderedServers)
                {
                    var rpc = server.DiscordRpc ?? profile.DiscordRpc;
                    var effectiveRpcEnabled = (rpc?.Enabled ?? false) && discordRpcEnabled;
                    var effectiveRpcDetails = discordRpcPrivacyMode ? string.Empty : (rpc?.DetailsText ?? string.Empty);
                    var effectiveRpcState = discordRpcPrivacyMode ? string.Empty : (rpc?.StateText ?? string.Empty);
                    var effectiveRpcLargeText = discordRpcPrivacyMode ? string.Empty : (rpc?.LargeImageText ?? string.Empty);
                    var effectiveRpcSmallText = discordRpcPrivacyMode ? string.Empty : (rpc?.SmallImageText ?? string.Empty);
                    var icon = await ResolveServerIconAsync(ApiBaseUrl, server.IconUrl, profile.IconUrl);
                    allServers.Add(new ManagedServerItem
                    {
                        ServerId = server.Id,
                        ProfileSlug = profile.Slug,
                        ProfileName = profile.Name,
                        ServerName = server.Name,
                        Address = server.Address,
                        Port = server.Port,
                        MainAddress = server.Address,
                        MainPort = server.Port,
                        MainJarPath = server.MainJarPath,
                        RuProxyAddress = server.RuProxyAddress,
                        RuProxyPort = server.RuProxyPort,
                        RuJarPath = server.RuJarPath,
                        LoaderType = server.LoaderType,
                        McVersion = server.McVersion,
                        RecommendedRamMb = profile.RecommendedRamMb,
                        DiscordRpcAppId = rpc?.AppId ?? string.Empty,
                        DiscordRpcDetails = effectiveRpcDetails,
                        DiscordRpcState = effectiveRpcState,
                        DiscordRpcLargeImageKey = rpc?.LargeImageKey ?? string.Empty,
                        DiscordRpcLargeImageText = effectiveRpcLargeText,
                        DiscordRpcSmallImageKey = rpc?.SmallImageKey ?? string.Empty,
                        DiscordRpcSmallImageText = effectiveRpcSmallText,
                        DiscordRpcEnabled = effectiveRpcEnabled,
                        DiscordPreview = BuildDiscordPreview(
                            effectiveRpcEnabled,
                            rpc?.AppId ?? string.Empty,
                            effectiveRpcDetails,
                            effectiveRpcState),
                        Icon = icon
                    });
                }
            }

            var allNews = bootstrap.News
                .OrderByDescending(item => item.Pinned)
                .ThenByDescending(item => item.CreatedAtUtc)
                .Select(item => new LauncherNewsItem
                {
                    Id = item.Id,
                    Title = item.Title,
                    Body = item.Body,
                    Preview = BuildNewsPreview(item.Body),
                    Source = item.Source,
                    Pinned = item.Pinned,
                    CreatedAtUtc = item.CreatedAtUtc,
                    Meta = BuildNewsMeta(item.Source, item.Pinned, item.CreatedAtUtc)
                })
                .ToList();

            ManagedServers.Clear();
            foreach (var server in allServers)
            {
                ManagedServers.Add(server);
            }

            NewsItems.Clear();
            foreach (var item in allNews)
            {
                NewsItems.Add(item);
            }

            SelectedNewsItem = NewsItems.FirstOrDefault();

            if (ManagedServers.Count == 0)
            {
                SelectedServer = null;
                _discordRpcService.ClearPresence();
                StatusText = T("status.noServers");
                return;
            }

            SelectedServer = ManagedServers.FirstOrDefault(x => x.ServerId.ToString() == _settings.SelectedServerId)
                ?? ManagedServers[0];

            StatusText = F("status.loadedServers", ManagedServers.Count);
        });
    }

    private async Task VerifyFilesAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            HasCrash = false;
            CrashSummary = string.Empty;

            StatusText = T("status.fetchingManifest");
            var manifest = await _launcherApiService.GetManifestAsync(ApiBaseUrl, SelectedServer.ProfileSlug);

            var progress = new Progress<InstallProgressInfo>(info =>
            {
                var currentPath = string.IsNullOrWhiteSpace(info.CurrentFilePath) ? info.Message : info.CurrentFilePath;
                StatusText = F("status.verifyingProgress", info.ProcessedFiles, info.TotalFiles, currentPath);
            });

            var result = await _manifestInstallerService.VerifyAndInstallAsync(
                ApiBaseUrl,
                manifest,
                InstallDirectory,
                progress);

            StatusText = F("status.verifyComplete", result.DownloadedFiles, result.VerifiedFiles);
            _logService.LogInfo(StatusText);
        });
    }

    private async Task LaunchAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            HasCrash = false;
            CrashSummary = string.Empty;

            var selectedServer = SelectedServer;
            LauncherManifest? manifest = null;
            GameLaunchRoute? launchRoute = null;
            LaunchResult? launchResult = null;
            Exception? launchException = null;
            var occurredAtUtc = DateTime.UtcNow;

            try
            {
                await SaveSettingsAsync();

                StatusText = T("status.fetchingManifest");
                manifest = await _launcherApiService.GetManifestAsync(ApiBaseUrl, selectedServer.ProfileSlug);

                var progress = new Progress<InstallProgressInfo>(info =>
                {
                    var currentPath = string.IsNullOrWhiteSpace(info.CurrentFilePath) ? info.Message : info.CurrentFilePath;
                    StatusText = F("status.verifyingProgress", info.ProcessedFiles, info.TotalFiles, currentPath);
                });

                var installResult = await _manifestInstallerService.VerifyAndInstallAsync(
                    ApiBaseUrl,
                    manifest,
                    InstallDirectory,
                    progress);

                StatusText = T("status.launchingJava");
                launchRoute = ResolveLaunchRoute(selectedServer);
                _discordRpcService.SetLaunchingPresence(selectedServer);

                try
                {
                    _discordRpcService.SetInGamePresence(selectedServer);
                    launchResult = await _gameLaunchService.LaunchAsync(
                        manifest,
                        BuildSettingsSnapshot(),
                        launchRoute,
                        installResult.InstanceDirectory,
                        line => _logService.LogInfo(line));
                }
                finally
                {
                    _discordRpcService.UpdateIdlePresence(selectedServer);
                }

                if (launchResult.Success)
                {
                    StatusText = T("status.gameExitedNormally");
                    return;
                }
            }
            catch (Exception ex)
            {
                launchException = ex;
                _logService.LogError(ex.ToString());
            }

            HasCrash = true;
            var fullLogExcerpt = string.Join(Environment.NewLine, _logService.GetRecentLines(120));
            var crashLines = _logService.GetRecentLines(60);
            CrashSummary = string.Join(Environment.NewLine, crashLines);

            var reason = BuildCrashReason(launchResult?.ExitCode, launchException);
            StatusText = launchResult is not null
                ? F("status.gameExitedCode", launchResult.ExitCode)
                : F("status.error", reason);

            var crashId = await TrySubmitCrashReportAsync(
                selectedServer,
                manifest,
                launchRoute,
                launchResult,
                launchException,
                fullLogExcerpt,
                occurredAtUtc);

            if (!string.IsNullOrWhiteSpace(crashId))
            {
                StatusText = $"{StatusText} Crash ID: {crashId}";
            }
        });
    }

    private async Task<string> TrySubmitCrashReportAsync(
        ManagedServerItem server,
        LauncherManifest? manifest,
        GameLaunchRoute? launchRoute,
        LaunchResult? launchResult,
        Exception? launchException,
        string logExcerpt,
        DateTime occurredAtUtc)
    {
        var reason = BuildCrashReason(launchResult?.ExitCode, launchException);
        var errorType = launchException?.GetType().Name ?? InferCrashErrorType(reason, logExcerpt);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["loaderType"] = manifest?.LoaderType ?? server.LoaderType,
            ["mcVersion"] = manifest?.McVersion ?? server.McVersion,
            ["launchMode"] = manifest?.LaunchMode ?? string.Empty,
            ["routeAddress"] = launchRoute?.Address ?? string.Empty,
            ["routePort"] = launchRoute?.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["javaExecutable"] = launchResult?.JavaExecutable ?? string.Empty
        }
        .Where(x => !string.IsNullOrWhiteSpace(x.Value))
        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        var request = new PublicCrashReportCreateRequest
        {
            ProfileSlug = server.ProfileSlug,
            ServerName = server.ServerName,
            RouteCode = launchRoute?.RouteCode ?? GetSelectedRouteCode(server.ProfileSlug),
            LauncherVersion = _currentLauncherVersion,
            OsVersion = Environment.OSVersion.VersionString,
            JavaVersion = ResolveJavaVersion(launchResult, manifest, logExcerpt),
            ExitCode = launchResult?.ExitCode,
            Reason = reason,
            ErrorType = errorType,
            LogExcerpt = logExcerpt,
            OccurredAtUtc = occurredAtUtc,
            Metadata = JsonSerializer.SerializeToElement(metadata)
        };

        try
        {
            var response = await _launcherApiService.SubmitCrashReportAsync(ApiBaseUrl, request);

            _logService.LogInfo($"Crash report sent: {response.CrashId}");
            return response.CrashId;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Crash report upload failed: {ex.Message}");

            try
            {
                await _pendingSubmissionService.EnqueueCrashReportAsync(ApiBaseUrl, request);
                _logService.LogInfo("Crash report queued for retry.");
            }
            catch (Exception queueEx)
            {
                _logService.LogError($"Crash report queueing failed: {queueEx.Message}");
            }

            return string.Empty;
        }
    }

    private async Task TrySubmitInstallTelemetryAsync(BootstrapResponse bootstrap)
    {
        if (_installTelemetrySent || !bootstrap.Constraints.InstallTelemetryEnabled)
        {
            return;
        }

        var projectName = string.IsNullOrWhiteSpace(bootstrap.Branding.ProductName)
            ? "unknown"
            : bootstrap.Branding.ProductName.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return;
        }

        var request = new PublicInstallTelemetryTrackRequest
        {
            ProjectName = projectName,
            LauncherVersion = _currentLauncherVersion
        };

        try
        {
            var response = await _launcherApiService.SubmitInstallTelemetryAsync(ApiBaseUrl, request);

            if (response.Accepted || !response.Enabled)
            {
                _installTelemetrySent = true;
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Install telemetry upload failed: {ex.Message}");

            try
            {
                await _pendingSubmissionService.EnqueueInstallTelemetryAsync(ApiBaseUrl, request);
                _installTelemetrySent = true;
                _logService.LogInfo("Install telemetry queued for retry.");
            }
            catch (Exception queueEx)
            {
                _logService.LogError($"Install telemetry queueing failed: {queueEx.Message}");
            }
        }
    }

    private async Task FlushPendingSubmissionsAsync()
    {
        var result = await _pendingSubmissionService.FlushAsync(async (item, cancellationToken) =>
        {
            if (item.Type.Equals(PendingSubmissionTypes.CrashReport, StringComparison.OrdinalIgnoreCase))
            {
                if (item.CrashReport is null)
                {
                    return true;
                }

                var response = await _launcherApiService.SubmitCrashReportAsync(
                    item.ApiBaseUrl,
                    item.CrashReport,
                    cancellationToken);
                _logService.LogInfo($"Queued crash report sent: {response.CrashId}");
                return true;
            }

            if (item.Type.Equals(PendingSubmissionTypes.InstallTelemetry, StringComparison.OrdinalIgnoreCase))
            {
                if (item.InstallTelemetry is null)
                {
                    return true;
                }

                var response = await _launcherApiService.SubmitInstallTelemetryAsync(
                    item.ApiBaseUrl,
                    item.InstallTelemetry,
                    cancellationToken);

                if (response.Accepted || !response.Enabled)
                {
                    _installTelemetrySent = true;
                }

                return true;
            }

            return true;
        });

        if (result.SentCount == 0 && result.DroppedCount == 0)
        {
            return;
        }

        _logService.LogInfo(
            $"Pending submissions sync: sent={result.SentCount}, failed={result.FailedCount}, dropped={result.DroppedCount}, remaining={result.RemainingCount}.");
    }

    private static string BuildCrashReason(int? exitCode, Exception? launchException)
    {
        if (launchException is not null)
        {
            return $"{launchException.GetType().Name}: {launchException.Message}";
        }

        if (!exitCode.HasValue)
        {
            return "Unknown launch failure.";
        }

        return exitCode.Value switch
        {
            0 => "Process exited normally.",
            -1073740791 => "Invalid Java or game launch arguments.",
            -1073741819 => "Process access violation crash.",
            _ => $"Process exited with code {exitCode.Value}."
        };
    }

    private static string InferCrashErrorType(string reason, string logExcerpt)
    {
        var source = $"{reason} {logExcerpt}";
        if (source.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            return "Auth";
        }

        if (source.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Timeout";
        }

        if (source.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "Network";
        }

        if (source.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "MissingDependency";
        }

        return "ProcessExit";
    }

    private static string ResolveJavaVersion(LaunchResult? launchResult, LauncherManifest? manifest, string logExcerpt)
    {
        var fromLogs = TryExtractJavaVersionFromLogs(logExcerpt);
        if (!string.IsNullOrWhiteSpace(fromLogs))
        {
            return fromLogs;
        }

        var javaExecutable = launchResult?.JavaExecutable ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(javaExecutable) && File.Exists(javaExecutable))
        {
            try
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(javaExecutable);
                if (!string.IsNullOrWhiteSpace(fileVersion.ProductVersion))
                {
                    return fileVersion.ProductVersion.Trim();
                }

                if (!string.IsNullOrWhiteSpace(fileVersion.FileVersion))
                {
                    return fileVersion.FileVersion.Trim();
                }
            }
            catch
            {
            }
        }

        return manifest?.JavaRuntime?.Trim() ?? string.Empty;
    }

    private static string TryExtractJavaVersionFromLogs(string logExcerpt)
    {
        if (string.IsNullOrWhiteSpace(logExcerpt))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            logExcerpt,
            "(?im)\\b(?:java|openjdk)\\s+version\\s+\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups.Count < 2)
        {
            return string.Empty;
        }

        return match.Groups[1].Value.Trim();
    }

    private async Task SaveSettingsAsync()
    {
        _settings = BuildSettingsSnapshot();
        await _settingsService.SaveAsync(_settings);
        StatusText = T("status.settingsSaved");
    }

    private async Task CopyCrashAsync()
    {
        if (string.IsNullOrWhiteSpace(CrashSummary))
        {
            return;
        }

        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
            .MainWindow?
            .Clipboard;

        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(CrashSummary);
        StatusText = T("status.crashCopied");
    }

    private void OpenLogsFolder()
    {
        var logsDirectory = _settingsService.GetLogsDirectory();
        Directory.CreateDirectory(logsDirectory);

        Process.Start(new ProcessStartInfo
        {
            FileName = logsDirectory,
            UseShellExecute = true
        });
    }

    private LauncherSettings BuildSettingsSnapshot()
    {
        return new LauncherSettings
        {
            ApiBaseUrl = NormalizeBaseUrl(ApiBaseUrl),
            InstallDirectory = InstallDirectory.Trim(),
            DebugMode = DebugMode,
            RamMb = RamMb,
            JavaMode = JavaMode,
            Language = _languageCode,
            ProfileRouteSelections = _profileRouteSelections
                .Select(x => new ProfileRouteSelection
                {
                    ProfileSlug = x.Key,
                    Route = NormalizeRouteCode(x.Value)
                })
                .OrderBy(x => x.ProfileSlug, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SelectedServerId = SelectedServer?.ServerId.ToString() ?? string.Empty,
            LastPlayerUsername = PlayerUsername.Trim()
        };
    }

    private async Task LoginAsync()
    {
        await RunBusyAsync(async () =>
        {
            var username = PlayerUsername.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException(T("validation.usernameRequired"));
            }

            if (string.IsNullOrWhiteSpace(PlayerPassword))
            {
                throw new InvalidOperationException(T("validation.passwordRequired"));
            }

            AuthStatusText = T("status.authorizing");
            StatusText = AuthStatusText;

            var response = await _launcherApiService.LoginAsync(ApiBaseUrl, new PublicAuthLoginRequest
            {
                Username = username,
                Password = PlayerPassword,
                HwidFingerprint = ComputeHwidFingerprint(),
                TwoFactorCode = PlayerTwoFactorCode.Trim()
            });

            if (response.RequiresTwoFactor)
            {
                IsTwoFactorStepActive = true;
                TwoFactorSetupSecret = response.TwoFactorSecret.Trim();
                TwoFactorSetupUri = response.TwoFactorProvisioningUri.Trim();
                AuthStatusText = string.IsNullOrWhiteSpace(response.Message)
                    ? T("status.twoFactorRequired")
                    : response.Message.Trim();
                StatusText = AuthStatusText;
                return;
            }

            IsPlayerLoggedIn = true;
            IsSettingsOpen = false;
            IsTwoFactorStepActive = false;
            TwoFactorSetupSecret = string.Empty;
            TwoFactorSetupUri = string.Empty;
            PlayerTwoFactorCode = string.Empty;
            PlayerLoggedInAs = response.Username;
            PlayerPassword = string.Empty;
            var hasSkin = await _launcherApiService.HasSkinAsync(ApiBaseUrl, response.Username);
            var hasCape = await _launcherApiService.HasCapeAsync(ApiBaseUrl, response.Username);
            HasSkin = hasSkin;
            HasCape = hasCape;
            AuthStatusText = F("status.loggedInAs", response.Username, string.Join(", ", response.Roles));
            StatusText = T("status.ready");
            _logService.LogInfo($"Player login success: {response.Username} ({response.ExternalId})");
        });
    }

    private void OnLogLineAdded(string line)
    {
        if (!DebugMode)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _liveLogLines.Enqueue(line);
            while (_liveLogLines.Count > MaxLiveLogLines)
            {
                _liveLogLines.Dequeue();
            }

            LiveLogs = string.Join(Environment.NewLine, _liveLogLines);
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            StatusText = BuildStatusErrorText(ex);
            _logService.LogError(ex.ToString());
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsPlayerLoggedInChanged(bool value)
    {
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        ToggleSettingsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsLoginRequired));
        OnPropertyChanged(nameof(IsLauncherReady));

        if (!value)
        {
            IsSettingsOpen = false;
            IsTwoFactorStepActive = false;
            TwoFactorSetupSecret = string.Empty;
            TwoFactorSetupUri = string.Empty;
            PlayerTwoFactorCode = string.Empty;
            HasSkin = false;
            HasCape = false;
            AuthStatusText = T("status.notLoggedIn");
        }
    }

    private bool CanToggleSettings()
    {
        return IsLauncherReady && !IsBusy;
    }

    private void ToggleSettings()
    {
        if (!CanToggleSettings())
        {
            IsSettingsOpen = false;
            return;
        }

        IsSettingsOpen = !IsSettingsOpen;
    }

    private bool CanOpenUpdateUrl()
    {
        return IsUpdateAvailable && !string.IsNullOrWhiteSpace(UpdateDownloadUrl);
    }

    private bool CanDownloadUpdate()
    {
        return !IsBusy &&
               !IsUpdateDownloading &&
               IsUpdateAvailable &&
               !string.IsNullOrWhiteSpace(UpdateDownloadUrl);
    }

    private bool CanInstallUpdate()
    {
        return !IsBusy &&
               !IsUpdateDownloading &&
               IsUpdateAvailable &&
               IsUpdatePackageReady;
    }

    private async Task DownloadUpdateAsync()
    {
        if (!CanDownloadUpdate())
        {
            return;
        }

        try
        {
            IsUpdateDownloading = true;
            UpdateDownloadStatusText = "Preparing update download...";
            UpdateDownloadProgressPercent = 0;

            var progress = new Progress<LauncherUpdateDownloadProgress>(info =>
            {
                if (info.TotalBytes is > 0)
                {
                    UpdateDownloadProgressPercent = (int)Math.Clamp(
                        (double)info.DownloadedBytes / info.TotalBytes.Value * 100d,
                        0d,
                        100d);
                }

                var downloadedMb = info.DownloadedBytes / (1024d * 1024d);
                if (info.TotalBytes is > 0)
                {
                    var totalMb = info.TotalBytes.Value / (1024d * 1024d);
                    UpdateDownloadStatusText = $"Downloaded {downloadedMb:F1}/{totalMb:F1} MB";
                }
                else
                {
                    UpdateDownloadStatusText = $"Downloaded {downloadedMb:F1} MB";
                }
            });

            var packagePath = await _launcherUpdateService.DownloadPackageAsync(
                UpdateDownloadUrl,
                LatestLauncherVersion,
                progress);

            DownloadedUpdatePackagePath = packagePath;
            DownloadedUpdateVersion = LatestLauncherVersion;
            UpdateDownloadProgressPercent = 100;
            UpdateDownloadStatusText = "Update package downloaded. Ready to install.";
            StatusText = "Update package downloaded.";
        }
        catch (Exception ex)
        {
            UpdateDownloadStatusText = $"Download failed: {ex.Message}";
            StatusText = BuildStatusErrorText(ex);
            _logService.LogError(ex.ToString());
        }
        finally
        {
            IsUpdateDownloading = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (!CanInstallUpdate())
        {
            return;
        }

        try
        {
            await SaveSettingsAsync();

            var executablePath = ResolveLauncherExecutablePath();
            _launcherUpdateService.ScheduleInstallAndRestart(DownloadedUpdatePackagePath, executablePath);

            UpdateDownloadStatusText = "Installer started. Launcher will close for update.";
            StatusText = "Applying launcher update...";

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            StatusText = BuildStatusErrorText(ex);
            UpdateDownloadStatusText = $"Install failed: {ex.Message}";
            _logService.LogError(ex.ToString());
        }
    }

    private void OpenUpdateUrl()
    {
        if (!CanOpenUpdateUrl())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateDownloadUrl.Trim(),
                UseShellExecute = true
            });
            StatusText = "Opened launcher update page.";
        }
        catch (Exception ex)
        {
            StatusText = BuildStatusErrorText(ex);
            _logService.LogError(ex.ToString());
        }
    }

    private static string ResolveLauncherExecutablePath()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            File.Exists(processPath) &&
            !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var appHostPath = Path.Combine(AppContext.BaseDirectory, "BivLauncher.Client.exe");
        if (File.Exists(appHostPath))
        {
            return appHostPath;
        }

        throw new InvalidOperationException("Cannot determine launcher executable path. Run published launcher build.");
    }

    private async Task ApplyBrandingAsync(string apiBaseUrl, BrandingConfig? branding)
    {
        branding ??= new BrandingConfig();

        ProductName = string.IsNullOrWhiteSpace(branding.ProductName)
            ? "BivLauncher"
            : branding.ProductName.Trim();
        Tagline = string.IsNullOrWhiteSpace(branding.Tagline)
            ? T("tagline.default")
            : branding.Tagline.Trim();

        var primary = ParseColorOrFallback(branding.PrimaryColor, "#2F6FED");
        var accent = ParseColorOrFallback(branding.AccentColor, "#20C997");
        var deep = ParseColorOrFallback("#07111E", "#07111E");
        var panelBase = ParseColorOrFallback("#121F34", "#121F34");
        var cardBorderBase = ParseColorOrFallback("#2E557F", "#2E557F");

        LauncherBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(BlendColors(primary, deep, 0.78), 0),
                new GradientStop(BlendColors(primary, deep, 0.88), 0.55),
                new GradientStop(BlendColors(accent, deep, 0.9), 1)
            }
        };

        HeroBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(BlendColors(primary, Colors.White, 0.18), 0),
                new GradientStop(BlendColors(primary, deep, 0.42), 1)
            }
        };

        HeroBorderBrush = new SolidColorBrush(BlendColors(primary, accent, 0.35));
        LoginCardBackgroundBrush = new SolidColorBrush(BlendColors(panelBase, primary, 0.24));
        LoginCardBorderBrush = new SolidColorBrush(BlendColors(cardBorderBase, primary, 0.35));
        PlayButtonBackgroundBrush = new SolidColorBrush(BlendColors(accent, deep, 0.28));
        PlayButtonBorderBrush = new SolidColorBrush(BlendColors(accent, Colors.White, 0.26));

        BrandingBackgroundOverlayOpacity = Math.Clamp(branding.BackgroundOverlayOpacity, 0, 0.95);
        LoginCardHorizontalAlignment = ParseLoginCardAlignment(branding.LoginCardPosition);

        var requestedWidth = branding.LoginCardWidth <= 0 ? 460 : branding.LoginCardWidth;
        LoginCardWidth = Math.Clamp(requestedWidth, 340, 640);

        BrandingBackgroundImage = await ResolveBrandingImageAsync(apiBaseUrl, branding.BackgroundImageUrl);
    }

    private static HorizontalAlignment ParseLoginCardAlignment(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
    }

    private static Color ParseColorOrFallback(string? value, string fallbackHex)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Color.TryParse(value.Trim(), out var parsed))
        {
            return parsed;
        }

        return Color.Parse(fallbackHex);
    }

    private static Color BlendColors(Color from, Color to, double ratio)
    {
        var clamped = Math.Clamp(ratio, 0, 1);
        return Color.FromArgb(
            Lerp(from.A, to.A, clamped),
            Lerp(from.R, to.R, clamped),
            Lerp(from.G, to.G, clamped),
            Lerp(from.B, to.B, clamped));
    }

    private static byte Lerp(byte from, byte to, double ratio)
    {
        var value = from + ((to - from) * ratio);
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private void ApplyLauncherUpdateInfo(LauncherUpdateInfo? updateInfo)
    {
        LatestLauncherVersion = string.Empty;
        UpdateDownloadUrl = string.Empty;
        UpdateReleaseNotes = string.Empty;
        IsUpdateAvailable = false;
        UpdateDownloadProgressPercent = 0;

        if (updateInfo is null)
        {
            return;
        }

        var latestRaw = updateInfo.LatestVersion.Trim();
        var latestNormalized = NormalizeVersionString(latestRaw);
        var currentNormalized = NormalizeVersionString(_currentLauncherVersion);

        LatestLauncherVersion = string.IsNullOrWhiteSpace(latestRaw) ? string.Empty : latestRaw;
        UpdateDownloadUrl = updateInfo.DownloadUrl.Trim();
        UpdateReleaseNotes = updateInfo.ReleaseNotes.Trim();

        if (!TryParseVersion(latestNormalized, out var latestVersion) ||
            !TryParseVersion(currentNormalized, out var currentVersion))
        {
            if (!string.Equals(DownloadedUpdateVersion, LatestLauncherVersion, StringComparison.OrdinalIgnoreCase))
            {
                DownloadedUpdateVersion = string.Empty;
                DownloadedUpdatePackagePath = string.Empty;
            }
            return;
        }

        IsUpdateAvailable = latestVersion > currentVersion && !string.IsNullOrWhiteSpace(UpdateDownloadUrl);
        if (!IsUpdateAvailable)
        {
            DownloadedUpdateVersion = string.Empty;
            DownloadedUpdatePackagePath = string.Empty;
            UpdateDownloadStatusText = string.Empty;
            return;
        }

        if (!string.Equals(DownloadedUpdateVersion, LatestLauncherVersion, StringComparison.OrdinalIgnoreCase))
        {
            DownloadedUpdateVersion = string.Empty;
            DownloadedUpdatePackagePath = string.Empty;
            UpdateDownloadStatusText = string.Empty;
        }
    }

    private static string NormalizeVersionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var separatorIndex = trimmed.IndexOfAny(['-', '+']);
        return separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
    }

    private static bool TryParseVersion(string value, out Version parsed)
    {
        if (Version.TryParse(value, out var version) && version is not null)
        {
            parsed = version;
            return true;
        }

        parsed = new Version(0, 0, 0, 0);
        return false;
    }

    private static string GetCurrentLauncherVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var informationalNormalized = NormalizeVersionString(informational);
        if (Version.TryParse(informationalNormalized, out var informationalVersion))
        {
            return informationalVersion.ToString(3);
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }

    private static string NormalizeBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "http://localhost:8080";
        }

        return value.Trim().TrimEnd('/');
    }

    private static string NormalizeJavaMode(string? javaMode)
    {
        if (string.Equals(javaMode, "Bundled", StringComparison.OrdinalIgnoreCase))
        {
            return "Bundled";
        }

        if (string.Equals(javaMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            return "System";
        }

        return "Auto";
    }

    private static int GetTotalAvailableMemoryMb()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (bytes > 0 && bytes < long.MaxValue / 4)
            {
                return (int)(bytes / (1024 * 1024));
            }
        }
        catch
        {
        }

        return 8192;
    }

    private static string ComputeHwidFingerprint()
    {
        var raw = string.Join('|',
            NormalizeHwidPart(Environment.MachineName),
            NormalizeHwidPart(Environment.UserDomainName),
            NormalizeHwidPart(Environment.OSVersion.VersionString),
            Environment.ProcessorCount.ToString(),
            Environment.Is64BitOperatingSystem ? "x64" : "x86");

        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeHwidPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Trim().ToLowerInvariant();
    }

    private string BuildTwoFactorHintText()
    {
        if (!IsTwoFactorStepActive)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(TwoFactorSetupSecret))
        {
            return T("status.twoFactorRequired");
        }

        if (string.IsNullOrWhiteSpace(TwoFactorSetupUri))
        {
            return F("status.twoFactorSetupSecret", TwoFactorSetupSecret);
        }

        return F("status.twoFactorSetup", TwoFactorSetupSecret, TwoFactorSetupUri);
    }

    private string BuildStatusErrorText(Exception ex)
    {
        if (ex is LauncherApiException apiException &&
            apiException.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (apiException.RetryAfter is TimeSpan retryAfter &&
                retryAfter > TimeSpan.Zero)
            {
                var retrySeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                return F("status.rateLimitedRetry", retrySeconds);
            }

            return T("status.rateLimited");
        }

        return F("status.error", ex.Message);
    }

    private static string BuildNewsPreview(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var normalized = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length > 180 ? $"{normalized[..180]}..." : normalized;
    }

    private string BuildNewsMeta(string sourceRaw, bool pinned, DateTime createdAtUtc)
    {
        var source = string.IsNullOrWhiteSpace(sourceRaw) ? "manual" : sourceRaw.Trim();
        if (source.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            source = T("news.source.manual");
        }

        var localTime = createdAtUtc.ToLocalTime().ToString("g");
        return pinned
            ? F("news.meta.pinned", source, localTime)
            : F("news.meta.regular", source, localTime);
    }

    private string BuildDiscordPreview(bool enabled, string appId, string details, string state)
    {
        if (!enabled || string.IsNullOrWhiteSpace(appId))
        {
            return T("rpc.disabled");
        }

        var text = string.Join(' ', new[] { details, state }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        return F("rpc.preview", appId.Trim(), text);
    }

    private async Task<IImage?> ResolveServerIconAsync(string apiBaseUrl, string? serverIconUrl, string? profileIconUrl)
    {
        var candidates = new[] { serverIconUrl, profileIconUrl };
        foreach (var candidate in candidates)
        {
            var absoluteUrl = ToAbsoluteUrl(apiBaseUrl, candidate);
            if (string.IsNullOrWhiteSpace(absoluteUrl))
            {
                continue;
            }

            if (_iconCache.TryGetValue(absoluteUrl, out var cached))
            {
                return cached;
            }

            try
            {
                await using var stream = await _iconHttpClient.GetStreamAsync(absoluteUrl);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                memory.Position = 0;

                var bitmap = new Bitmap(memory);
                _iconCache[absoluteUrl] = bitmap;
                return bitmap;
            }
            catch
            {
                _iconCache[absoluteUrl] = null;
            }
        }

        return null;
    }

    private async Task<IImage?> ResolveBrandingImageAsync(string apiBaseUrl, string? brandingImageUrl)
    {
        var absoluteUrl = ToAbsoluteUrl(apiBaseUrl, brandingImageUrl);
        if (string.IsNullOrWhiteSpace(absoluteUrl))
        {
            return null;
        }

        if (_brandingImageCache.TryGetValue(absoluteUrl, out var cached))
        {
            return cached;
        }

        try
        {
            await using var stream = await _iconHttpClient.GetStreamAsync(absoluteUrl);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;

            var bitmap = new Bitmap(memory);
            _brandingImageCache[absoluteUrl] = bitmap;
            return bitmap;
        }
        catch
        {
            _brandingImageCache[absoluteUrl] = null;
            return null;
        }
    }

    private static string ToAbsoluteUrl(string apiBaseUrl, string? maybeUrl)
    {
        if (string.IsNullOrWhiteSpace(maybeUrl))
        {
            return string.Empty;
        }

        var trimmed = maybeUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUri = new Uri($"{NormalizeBaseUrl(apiBaseUrl)}/");
        return new Uri(baseUri, trimmed.TrimStart('/')).ToString();
    }

    private static string NormalizeRouteCode(string? routeCode)
    {
        return string.Equals(routeCode, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "main";
    }

    private void LoadRouteSelections(IEnumerable<ProfileRouteSelection> selections)
    {
        _profileRouteSelections.Clear();
        foreach (var selection in selections)
        {
            var profileSlug = selection.ProfileSlug.Trim();
            if (string.IsNullOrWhiteSpace(profileSlug))
            {
                continue;
            }

            _profileRouteSelections[profileSlug] = NormalizeRouteCode(selection.Route);
        }
    }

    private string GetSelectedRouteCode(string profileSlug)
    {
        return _profileRouteSelections.TryGetValue(profileSlug, out var route) ? NormalizeRouteCode(route) : "main";
    }

    private void SyncSelectedRouteOption()
    {
        _isSyncingRouteOption = true;
        if (SelectedServer is null)
        {
            SelectedRouteOption = null;
        }
        else
        {
            var route = GetSelectedRouteCode(SelectedServer.ProfileSlug);
            SelectedRouteOption = RouteOptions.FirstOrDefault(x => x.Value == route) ?? RouteOptions.FirstOrDefault();
        }
        _isSyncingRouteOption = false;
    }

    private void RebuildRouteOptions()
    {
        var selectedRoute = SelectedServer is null ? "main" : GetSelectedRouteCode(SelectedServer.ProfileSlug);
        _isSyncingRouteOption = true;
        RouteOptions.Clear();
        RouteOptions.Add(new LocalizedOption { Value = "main", Label = T("route.main") });
        RouteOptions.Add(new LocalizedOption { Value = "ru", Label = T("route.ru") });
        SelectedRouteOption = RouteOptions.FirstOrDefault(x => x.Value == selectedRoute) ?? RouteOptions[0];
        _isSyncingRouteOption = false;
    }

    private GameLaunchRoute ResolveLaunchRoute(ManagedServerItem server)
    {
        var routeCode = GetSelectedRouteCode(server.ProfileSlug);

        if (routeCode == "ru")
        {
            if (string.IsNullOrWhiteSpace(server.RuProxyAddress))
            {
                throw new InvalidOperationException(T("validation.ruProxyAddressRequired"));
            }

            return new GameLaunchRoute
            {
                RouteCode = "ru",
                Address = server.RuProxyAddress.Trim(),
                Port = server.RuProxyPort,
                PreferredJarPath = server.RuJarPath.Trim()
            };
        }

        return new GameLaunchRoute
        {
            RouteCode = "main",
            Address = server.MainAddress.Trim(),
            Port = server.MainPort,
            PreferredJarPath = server.MainJarPath.Trim()
        };
    }

    private void RebuildJavaModeOptions()
    {
        var currentMode = NormalizeJavaMode(JavaMode);
        _isSyncingJavaModeOption = true;
        JavaModeOptions.Clear();
        JavaModeOptions.Add(new LocalizedOption { Value = "Auto", Label = T("java.auto") });
        JavaModeOptions.Add(new LocalizedOption { Value = "Bundled", Label = T("java.bundled") });
        JavaModeOptions.Add(new LocalizedOption { Value = "System", Label = T("java.system") });
        SelectedJavaModeOption = JavaModeOptions.FirstOrDefault(x => x.Value == currentMode) ?? JavaModeOptions[0];
        _isSyncingJavaModeOption = false;
        JavaMode = currentMode;
    }

    private void SyncSelectedJavaModeOption()
    {
        var currentMode = NormalizeJavaMode(JavaMode);
        _isSyncingJavaModeOption = true;
        SelectedJavaModeOption = JavaModeOptions.FirstOrDefault(x => x.Value == currentMode) ?? JavaModeOptions.FirstOrDefault();
        _isSyncingJavaModeOption = false;
    }

    private void SyncSelectedLanguageOption()
    {
        _isSyncingLanguageOption = true;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(x => x.Value == _languageCode)
            ?? LanguageOptions.FirstOrDefault(x => x.Value == "ru")
            ?? LanguageOptions.FirstOrDefault();
        _isSyncingLanguageOption = false;
    }

    private void RelocalizeCollections()
    {
        var selectedServerId = SelectedServer?.ServerId;
        var selectedNewsId = SelectedNewsItem?.Id;

        var localizedServers = ManagedServers.Select(server => new ManagedServerItem
        {
            ServerId = server.ServerId,
            ProfileSlug = server.ProfileSlug,
            ProfileName = server.ProfileName,
            ServerName = server.ServerName,
            Address = server.Address,
            Port = server.Port,
            MainAddress = server.MainAddress,
            MainPort = server.MainPort,
            MainJarPath = server.MainJarPath,
            RuProxyAddress = server.RuProxyAddress,
            RuProxyPort = server.RuProxyPort,
            RuJarPath = server.RuJarPath,
            LoaderType = server.LoaderType,
            McVersion = server.McVersion,
            RecommendedRamMb = server.RecommendedRamMb,
            DiscordRpcAppId = server.DiscordRpcAppId,
            DiscordRpcDetails = server.DiscordRpcDetails,
            DiscordRpcState = server.DiscordRpcState,
            DiscordRpcLargeImageKey = server.DiscordRpcLargeImageKey,
            DiscordRpcLargeImageText = server.DiscordRpcLargeImageText,
            DiscordRpcSmallImageKey = server.DiscordRpcSmallImageKey,
            DiscordRpcSmallImageText = server.DiscordRpcSmallImageText,
            DiscordRpcEnabled = server.DiscordRpcEnabled,
            DiscordPreview = BuildDiscordPreview(
                server.DiscordRpcEnabled,
                server.DiscordRpcAppId,
                server.DiscordRpcDetails,
                server.DiscordRpcState),
            Icon = server.Icon
        }).ToList();

        ManagedServers.Clear();
        foreach (var server in localizedServers)
        {
            ManagedServers.Add(server);
        }

        if (selectedServerId.HasValue)
        {
            SelectedServer = ManagedServers.FirstOrDefault(x => x.ServerId == selectedServerId.Value);
        }

        var localizedNews = NewsItems.Select(item => new LauncherNewsItem
        {
            Id = item.Id,
            Title = item.Title,
            Body = item.Body,
            Preview = item.Preview,
            Source = item.Source,
            Pinned = item.Pinned,
            CreatedAtUtc = item.CreatedAtUtc,
            Meta = BuildNewsMeta(item.Source, item.Pinned, item.CreatedAtUtc)
        }).ToList();

        NewsItems.Clear();
        foreach (var item in localizedNews)
        {
            NewsItems.Add(item);
        }

        if (selectedNewsId.HasValue)
        {
            SelectedNewsItem = NewsItems.FirstOrDefault(x => x.Id == selectedNewsId.Value);
        }
        else
        {
            SelectedNewsItem = NewsItems.FirstOrDefault();
        }
    }

    private void RefreshLocalizedBindings()
    {
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(SaveSettingsButtonText));
        OnPropertyChanged(nameof(ServersHeaderText));
        OnPropertyChanged(nameof(NewsHeaderText));
        OnPropertyChanged(nameof(SettingsHeaderText));
        OnPropertyChanged(nameof(ApiBaseUrlLabelText));
        OnPropertyChanged(nameof(InstallDirectoryLabelText));
        OnPropertyChanged(nameof(LanguageLabelText));
        OnPropertyChanged(nameof(AccountHeaderText));
        OnPropertyChanged(nameof(UsernameLabelText));
        OnPropertyChanged(nameof(PasswordLabelText));
        OnPropertyChanged(nameof(TwoFactorCodeLabelText));
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(TwoFactorHintText));
        OnPropertyChanged(nameof(SkinStatusText));
        OnPropertyChanged(nameof(CapeStatusText));
        OnPropertyChanged(nameof(RuntimeHeaderText));
        OnPropertyChanged(nameof(RouteLabelText));
        OnPropertyChanged(nameof(JavaModeLabelText));
        OnPropertyChanged(nameof(RamLabelText));
        OnPropertyChanged(nameof(RamMinText));
        OnPropertyChanged(nameof(RamMaxText));
        OnPropertyChanged(nameof(DebugModeLabelText));
        OnPropertyChanged(nameof(VerifyFilesButtonText));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(OpenLogsFolderButtonText));
        OnPropertyChanged(nameof(CrashSummaryHeaderText));
        OnPropertyChanged(nameof(CopyCrashButtonText));
        OnPropertyChanged(nameof(CrashFlagText));
        OnPropertyChanged(nameof(DebugLogHeaderText));
    }

    private string T(string key)
    {
        return LauncherLocalization.T(_languageCode, key);
    }

    private string F(string key, params object[] args)
    {
        return LauncherLocalization.F(_languageCode, key, args);
    }

    private string BoolWord(bool value)
    {
        return value ? T("common.yes") : T("common.no");
    }
}

