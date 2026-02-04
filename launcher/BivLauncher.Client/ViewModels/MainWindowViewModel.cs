using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BivLauncher.Client.Models;
using BivLauncher.Client.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BivLauncher.Client.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILauncherApiService _launcherApiService;
    private readonly IManifestInstallerService _manifestInstallerService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IDiscordRpcService _discordRpcService;
    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly ILogService _logService;
    private readonly HttpClient _iconHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Dictionary<string, IImage?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _liveLogLines = new();
    private const int MaxLiveLogLines = 500;
    private readonly string _currentLauncherVersion = GetCurrentLauncherVersion();
    private string _languageCode = "ru";
    private readonly Dictionary<string, string> _profileRouteSelections = new(StringComparer.OrdinalIgnoreCase);
    private bool _isSyncingLanguageOption;
    private bool _isSyncingJavaModeOption;
    private bool _isSyncingRouteOption;

    private LauncherSettings _settings = new();

    public MainWindowViewModel(
        ISettingsService settingsService,
        ILauncherApiService launcherApiService,
        IManifestInstallerService manifestInstallerService,
        IGameLaunchService gameLaunchService,
        IDiscordRpcService discordRpcService,
        ILauncherUpdateService launcherUpdateService,
        ILogService logService)
    {
        _settingsService = settingsService;
        _launcherApiService = launcherApiService;
        _manifestInstallerService = manifestInstallerService;
        _gameLaunchService = gameLaunchService;
        _discordRpcService = discordRpcService;
        _launcherUpdateService = launcherUpdateService;
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
    public IRelayCommand OpenUpdateUrlCommand { get; }
    public IAsyncRelayCommand DownloadUpdateCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }

    [ObservableProperty]
    private string _productName = "BivLauncher";

    [ObservableProperty]
    private string _tagline = "Управляемый лаунчер";

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
    private string _playerLoggedInAs = string.Empty;

    [ObservableProperty]
    private string _authStatusText = "Не выполнен вход.";

    [ObservableProperty]
    private bool _isPlayerLoggedIn;

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
    public string LoginButtonText => T("button.login");
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

            ProductName = string.IsNullOrWhiteSpace(bootstrap.Branding.ProductName)
                ? "BivLauncher"
                : bootstrap.Branding.ProductName;
            Tagline = string.IsNullOrWhiteSpace(bootstrap.Branding.Tagline)
                ? T("tagline.default")
                : bootstrap.Branding.Tagline;
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
                        DiscordRpcDetails = rpc?.DetailsText ?? string.Empty,
                        DiscordRpcState = rpc?.StateText ?? string.Empty,
                        DiscordRpcLargeImageKey = rpc?.LargeImageKey ?? string.Empty,
                        DiscordRpcLargeImageText = rpc?.LargeImageText ?? string.Empty,
                        DiscordRpcSmallImageKey = rpc?.SmallImageKey ?? string.Empty,
                        DiscordRpcSmallImageText = rpc?.SmallImageText ?? string.Empty,
                        DiscordRpcEnabled = rpc?.Enabled ?? false,
                        DiscordPreview = BuildDiscordPreview(
                            rpc?.Enabled ?? false,
                            rpc?.AppId ?? string.Empty,
                            rpc?.DetailsText ?? string.Empty,
                            rpc?.StateText ?? string.Empty),
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

            await SaveSettingsAsync();

            StatusText = T("status.fetchingManifest");
            var manifest = await _launcherApiService.GetManifestAsync(ApiBaseUrl, SelectedServer.ProfileSlug);

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
            var launchRoute = ResolveLaunchRoute(SelectedServer);
            _discordRpcService.SetLaunchingPresence(SelectedServer);
            LaunchResult launchResult;
            try
            {
                _discordRpcService.SetInGamePresence(SelectedServer);
                launchResult = await _gameLaunchService.LaunchAsync(
                    manifest,
                    BuildSettingsSnapshot(),
                    launchRoute,
                    installResult.InstanceDirectory,
                    line => _logService.LogInfo(line));
            }
            finally
            {
                _discordRpcService.UpdateIdlePresence(SelectedServer);
            }

            if (launchResult.Success)
            {
                StatusText = T("status.gameExitedNormally");
                return;
            }

            HasCrash = true;
            var recentLines = _logService.GetRecentLines(40);
            CrashSummary = string.Join(Environment.NewLine, recentLines);
            StatusText = F("status.gameExitedCode", launchResult.ExitCode);
        });
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

            var response = await _launcherApiService.LoginAsync(ApiBaseUrl, new PublicAuthLoginRequest
            {
                Username = username,
                Password = PlayerPassword,
                HwidFingerprint = ComputeHwidFingerprint()
            });

            IsPlayerLoggedIn = true;
            PlayerLoggedInAs = response.Username;
            PlayerPassword = string.Empty;
            var hasSkin = await _launcherApiService.HasSkinAsync(ApiBaseUrl, response.Username);
            var hasCape = await _launcherApiService.HasCapeAsync(ApiBaseUrl, response.Username);
            HasSkin = hasSkin;
            HasCape = hasCape;
            AuthStatusText = F("status.loggedInAs", response.Username, string.Join(", ", response.Roles));
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
            StatusText = F("status.error", ex.Message);
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

        if (!value)
        {
            HasSkin = false;
            HasCape = false;
            AuthStatusText = T("status.notLoggedIn");
        }
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
            StatusText = F("status.error", ex.Message);
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
            StatusText = F("status.error", ex.Message);
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
            StatusText = F("status.error", ex.Message);
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
        OnPropertyChanged(nameof(LoginButtonText));
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
