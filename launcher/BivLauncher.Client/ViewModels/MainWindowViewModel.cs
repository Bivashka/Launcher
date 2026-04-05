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
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
    private readonly HttpClient _iconHttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly Dictionary<string, IImage?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IImage?> _brandingImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> _windowIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _liveLogLines = new();
    private readonly SemaphoreSlim _serverOnlineRefreshLock = new(1, 1);
    private readonly object _assetCacheSyncRoot = new();
    private readonly DispatcherTimer _serverOnlineRefreshTimer;
    private const int MaxLiveLogLines = 500;
    private const int ServerOnlineTimeoutMs = 3500;
    private static readonly TimeSpan PendingSubmissionFlushTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PendingSubmissionSendTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ServerOnlineRefreshInterval = TimeSpan.FromSeconds(25);
    private const string LocalFallbackApiBaseUrl = "http://localhost:8080";
    private const string LauncherApiBaseUrlEnvVar = "BIVLAUNCHER_API_BASE_URL";
    private const string LauncherApiBaseUrlRuEnvVar = "BIVLAUNCHER_API_BASE_URL_RU";
    private const string LauncherApiBaseUrlEuEnvVar = "BIVLAUNCHER_API_BASE_URL_EU";
    private const string LauncherApiBaseUrlAssemblyMetadataKey = "BivLauncher.ApiBaseUrl";
    private const string LauncherApiBaseUrlRuAssemblyMetadataKey = "BivLauncher.ApiBaseUrlRu";
    private const string LauncherApiBaseUrlEuAssemblyMetadataKey = "BivLauncher.ApiBaseUrlEu";
    private const string LauncherFallbackApiBaseUrlsAssemblyMetadataKey = "BivLauncher.FallbackApiBaseUrls";
    private readonly string _currentLauncherVersion = GetCurrentLauncherVersion();
    private string _languageCode = "ru";
    private readonly Dictionary<string, string> _profileRouteSelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _knownApiBaseUrls = [];
    private readonly List<LauncherNewsItem> _allNewsItems = [];
    private bool _isSyncingLanguageOption;
    private bool _isSyncingJavaModeOption;
    private bool _isSyncingRouteOption;
    private bool _installTelemetrySent;
    private int _assetRefreshVersion;
    private bool _isAutomaticUpdateInProgress;
    private bool _allowAutoSessionRestore = true;
    private string _pendingTwoFactorUsername = string.Empty;
    private string _pendingTwoFactorPassword = string.Empty;
    private string _playerAuthToken = string.Empty;
    private string _playerAuthTokenType = "Bearer";
    private string _playerAuthExternalId = string.Empty;
    private List<string> _playerAuthRoles = [];
    private string _playerAuthApiBaseUrl = string.Empty;
    private readonly Dictionary<string, string> _profileBundledRuntimeKeys = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastServerOnlineRefreshUtc;
    private Avalonia.Controls.WindowIcon? _defaultWindowIcon;
    private int _busyOperationCount;

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
        StoredPlayerAccounts = new ObservableCollection<StoredPlayerAccount>();
        LanguageOptions = new ObservableCollection<LocalizedOption>(LauncherLocalization.SupportedLanguages);
        JavaModeOptions = new ObservableCollection<LocalizedOption>();
        RouteOptions = new ObservableCollection<LocalizedOption>();
        RebuildJavaModeOptions();
        RebuildRouteOptions();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanOperate);
        LoginCommand = new AsyncRelayCommand(LoginAsync, CanOperate);
        SwitchAccountCommand = new AsyncRelayCommand(SwitchAccountAsync, CanSwitchAccount);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, CanLogout);
        AddAccountCommand = new RelayCommand(AddAccount, CanAddAccount);
        DeleteAccountCommand = new AsyncRelayCommand(DeleteSelectedAccountAsync, CanDeleteAccount);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanOperate);
        VerifyFilesCommand = new AsyncRelayCommand(VerifyFilesAsync, CanVerifyOrLaunch);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, CanVerifyOrLaunch);
        CopyCrashCommand = new AsyncRelayCommand(CopyCrashAsync);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        ToggleSettingsCommand = new RelayCommand(ToggleSettings, CanToggleSettings);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        DismissBanNoticeCommand = new RelayCommand(ClearBanNotice);
        OpenUpdateUrlCommand = new RelayCommand(OpenUpdateUrl, CanOpenUpdateUrl);
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, CanDownloadUpdate);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, CanInstallUpdate);
        SelectRuApiRegionCommand = new AsyncRelayCommand(() => SelectApiRegionAsync("ru"), CanSelectRuApiRegion);
        SelectEuApiRegionCommand = new AsyncRelayCommand(() => SelectApiRegionAsync("eu"), CanSelectEuApiRegion);

        StoredPlayerAccounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasStoredAccounts));
            OnPropertyChanged(nameof(HasMultipleStoredAccounts));
            NotifyAccountPresentationChanged();
            OnPropertyChanged(nameof(ServerMonitoringText));
            SwitchAccountCommand.NotifyCanExecuteChanged();
            DeleteAccountCommand.NotifyCanExecuteChanged();
        };

        _serverOnlineRefreshTimer = new DispatcherTimer
        {
            Interval = ServerOnlineRefreshInterval
        };
        _serverOnlineRefreshTimer.Tick += async (_, _) => await RefreshServerOnlineStatusesAsync();

        _logService.LineAdded += OnLogLineAdded;
    }

    public ObservableCollection<ManagedServerItem> ManagedServers { get; }
    public ObservableCollection<LauncherNewsItem> NewsItems { get; }
    public ObservableCollection<StoredPlayerAccount> StoredPlayerAccounts { get; }
    public ObservableCollection<LocalizedOption> LanguageOptions { get; }
    public ObservableCollection<LocalizedOption> JavaModeOptions { get; }
    public ObservableCollection<LocalizedOption> RouteOptions { get; }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand SwitchAccountCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }
    public IRelayCommand AddAccountCommand { get; }
    public IAsyncRelayCommand DeleteAccountCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand VerifyFilesCommand { get; }
    public IAsyncRelayCommand LaunchCommand { get; }
    public IAsyncRelayCommand CopyCrashCommand { get; }
    public IRelayCommand OpenLogsFolderCommand { get; }
    public IRelayCommand ToggleSettingsCommand { get; }
    public IRelayCommand CloseSettingsCommand { get; }
    public IRelayCommand DismissBanNoticeCommand { get; }
    public IRelayCommand OpenUpdateUrlCommand { get; }
    public IAsyncRelayCommand DownloadUpdateCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }
    public IAsyncRelayCommand SelectRuApiRegionCommand { get; }
    public IAsyncRelayCommand SelectEuApiRegionCommand { get; }

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
            new GradientStop(Color.Parse("#22150F"), 0),
            new GradientStop(Color.Parse("#1A120F"), 0.55),
            new GradientStop(Color.Parse("#120D0B"), 1)
        }
    };

    [ObservableProperty]
    private IBrush _heroBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#5B341F"), 0),
            new GradientStop(Color.Parse("#7B4A2A"), 1)
        }
    };

    [ObservableProperty]
    private IBrush _heroBorderBrush = new SolidColorBrush(Color.Parse("#A66A42"));

    [ObservableProperty]
    private IBrush _loginCardBackgroundBrush = new SolidColorBrush(Color.Parse("#2B1E16"));

    [ObservableProperty]
    private IBrush _loginCardBorderBrush = new SolidColorBrush(Color.Parse("#7C583D"));

    [ObservableProperty]
    private IBrush _playButtonBackgroundBrush = new SolidColorBrush(Color.Parse("#149768"));

    [ObservableProperty]
    private IBrush _playButtonBorderBrush = new SolidColorBrush(Color.Parse("#59D1A2"));

    [ObservableProperty]
    private IBrush _playButtonForegroundBrush = new SolidColorBrush(Color.Parse("#F7FBFF"));

    [ObservableProperty]
    private IBrush _primaryButtonBackgroundBrush = new SolidColorBrush(Color.Parse("#2D1E22"));

    [ObservableProperty]
    private IBrush _primaryButtonBorderBrush = new SolidColorBrush(Color.Parse("#7E5A49"));

    [ObservableProperty]
    private IBrush _primaryButtonForegroundBrush = new SolidColorBrush(Color.Parse("#F7FBFF"));

    [ObservableProperty]
    private IBrush _panelBackgroundBrush = new SolidColorBrush(Color.Parse("#171117D9"));

    [ObservableProperty]
    private IBrush _panelBorderBrush = new SolidColorBrush(Color.Parse("#5E4135"));

    [ObservableProperty]
    private IBrush _inputBackgroundBrush = new SolidColorBrush(Color.Parse("#120D11E6"));

    [ObservableProperty]
    private IBrush _inputBorderBrush = new SolidColorBrush(Color.Parse("#64473B"));

    [ObservableProperty]
    private IBrush _inputForegroundBrush = new SolidColorBrush(Color.Parse("#FFF7F0"));

    [ObservableProperty]
    private IBrush _listBackgroundBrush = new SolidColorBrush(Color.Parse("#120D11C4"));

    [ObservableProperty]
    private IBrush _listBorderBrush = new SolidColorBrush(Color.Parse("#553B31"));

    [ObservableProperty]
    private IBrush _primaryTextBrush = new SolidColorBrush(Color.Parse("#FFF8F3"));

    [ObservableProperty]
    private IBrush _secondaryTextBrush = new SolidColorBrush(Color.Parse("#C8B3A3"));

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
    private bool _isGameSessionActive;

    [ObservableProperty]
    private string _apiBaseUrl = string.Empty;

    [ObservableProperty]
    private string _preferredApiRegion = string.Empty;

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
    private StoredPlayerAccount? _selectedStoredAccount;

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
    private bool _isBanNoticeVisible;

    [ObservableProperty]
    private string _banNoticeTitle = string.Empty;

    [ObservableProperty]
    private string _banNoticeMessage = string.Empty;

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

    [ObservableProperty]
    private bool _isFileSyncInProgress;

    [ObservableProperty]
    private string _fileSyncStageText = string.Empty;

    [ObservableProperty]
    private string _fileSyncCurrentFileText = string.Empty;

    [ObservableProperty]
    private int _fileSyncProgressPercent;

    [ObservableProperty]
    private int _fileSyncProcessedFiles;

    [ObservableProperty]
    private int _fileSyncTotalFiles;

    [ObservableProperty]
    private int _fileSyncDownloadedFiles;

    [ObservableProperty]
    private int _fileSyncVerifiedFiles;

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
    public string AddAccountButtonText => _languageCode == "en" ? "Add account" : "Добавить аккаунт";
    public string DeleteAccountButtonText => _languageCode == "en" ? "Remove account" : "Удалить аккаунт";
    public string LogoutButtonText => _languageCode == "en" ? "Logout" : "Выйти";
    public string SwitchAccountButtonText => _languageCode == "en" ? "Switch" : "Переключить";
    public string SavedAccountsLabelText => _languageCode == "en" ? "Saved accounts" : "Сохранённые аккаунты";
    public string BanNoticeEyebrowText => _languageCode == "en" ? "ACCESS RESTRICTED" : "ДОСТУП ОГРАНИЧЕН";
    public string BanNoticeDismissButtonText => _languageCode == "en" ? "Close notice" : "Закрыть уведомление";
    public string TwoFactorHintText => BuildTwoFactorHintText();
    public string SkinStatusText => F("status.skin", BoolWord(HasSkin));
    public string CapeStatusText => F("status.cape", BoolWord(HasCape));
    public string RuntimeHeaderText => T("header.runtime");
    public bool HasApiRegionChoice => !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion("ru")) &&
                                      !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion("eu"));
    public string ApiRegionHeaderText => _languageCode == "en" ? "Connection region" : "Регион подключения";
    public string ApiRegionHintText => _languageCode == "en"
        ? "Choose which API endpoint the launcher should try first."
        : "Выберите, к какому endpoint лаунчер должен подключаться в первую очередь.";
    public string ApiRegionRfButtonText => "🇷🇺";
    public string ApiRegionEuButtonText => "🇪🇺";
    public string ApiRegionRuToolTipText => _languageCode == "en" ? "RF route" : "Маршрут через РФ";
    public string ApiRegionEuToolTipText => _languageCode == "en" ? "EU route" : "Маршрут через EU";
    public bool IsRuApiRegionSelected => string.Equals(PreferredApiRegion, "ru", StringComparison.OrdinalIgnoreCase);
    public bool IsEuApiRegionSelected => string.Equals(PreferredApiRegion, "eu", StringComparison.OrdinalIgnoreCase);
    public IBrush ApiRegionRuBackgroundBrush => IsRuApiRegionSelected ? PrimaryButtonBackgroundBrush : InputBackgroundBrush;
    public IBrush ApiRegionRuBorderBrush => IsRuApiRegionSelected ? PrimaryButtonBorderBrush : InputBorderBrush;
    public IBrush ApiRegionRuForegroundBrush => IsRuApiRegionSelected ? PrimaryButtonForegroundBrush : PrimaryTextBrush;
    public IBrush ApiRegionEuBackgroundBrush => IsEuApiRegionSelected ? PrimaryButtonBackgroundBrush : InputBackgroundBrush;
    public IBrush ApiRegionEuBorderBrush => IsEuApiRegionSelected ? PrimaryButtonBorderBrush : InputBorderBrush;
    public IBrush ApiRegionEuForegroundBrush => IsEuApiRegionSelected ? PrimaryButtonForegroundBrush : PrimaryTextBrush;
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
    public string SelectedNewsMeta => SelectedNewsItem?.Meta ?? string.Empty;
    public string SelectedNewsBody => SelectedNewsItem?.Body ?? string.Empty;
    public string SelectedNewsHeadlineText => SelectedNewsItem?.Title ?? string.Empty;
    public string SelectedNewsBodyText => SelectedNewsItem?.Body ?? string.Empty;
    public string SelectedNewsMetaText => SelectedNewsItem?.Meta ?? string.Empty;
    public bool HasSelectedNews => SelectedNewsItem is not null;
    public bool HasSelectedNewsPlaceholder => !HasSelectedNews;
    public string EmptyNewsText => _languageCode == "en"
        ? "News for this branch will appear here."
        : "Новости для этой ветки появятся здесь.";
    public bool HasMultipleStoredAccounts => StoredPlayerAccounts.Count > 1;
    public string AccountPanelTitle => BuildAccountPanelTitle();
    public string AccountPanelSubtitle => BuildAccountPanelSubtitle();
    public bool HasAccountPanelSubtitle => !string.IsNullOrWhiteSpace(AccountPanelSubtitle);
    public bool HasAdminRoleBanner => IsPlayerLoggedIn && HasAdministrativeRole(_playerAuthRoles);
    public string AdminRoleBannerText => _languageCode == "en" ? "Administrator" : "Администратор";
    public string LauncherHeaderStatusText => BuildLauncherHeaderStatusText();
    public bool HasLauncherHeaderStatusText => !string.IsNullOrWhiteSpace(LauncherHeaderStatusText);
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
    public string FileSyncSummaryText => FileSyncTotalFiles > 0
        ? $"{FileSyncProcessedFiles}/{FileSyncTotalFiles}  Downloaded: {FileSyncDownloadedFiles}  Verified: {FileSyncVerifiedFiles}"
        : "Preparing file synchronization...";
    public string FileSyncPercentText => $"{FileSyncProgressPercent}%";
    public string ServerMonitoringText => BuildServerMonitoringText();
    public bool HasUpdateReleaseNotes => !string.IsNullOrWhiteSpace(UpdateReleaseNotes);
    public bool IsUpdatePackageReady => !string.IsNullOrWhiteSpace(DownloadedUpdatePackagePath) && File.Exists(DownloadedUpdatePackagePath);
    public bool HasBrandingBackgroundImage => BrandingBackgroundImage is not null;
    public bool HasStoredAccounts => StoredPlayerAccounts.Count > 0;
    public bool IsLoginRequired => !IsPlayerLoggedIn;
    public bool IsLauncherReady => IsPlayerLoggedIn;

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();
        PreferredApiRegion = ResolveInitialApiRegion(_settings.PreferredApiRegion);
        var configuredApiBaseUrl = TryResolveConfiguredApiBaseUrl();
        if (TrimConfiguredApiBaseUrlReferences(_settings, configuredApiBaseUrl))
        {
            await _settingsService.SaveAsync(_settings);
        }

        _languageCode = LauncherLocalization.NormalizeLanguage(_settings.Language);
        SyncSelectedLanguageOption();
        RebuildJavaModeOptions();
        RebuildRouteOptions();
        RefreshLocalizedBindings();
        LoadRouteSelections(_settings.ProfileRouteSelections ?? []);
        MergeKnownApiBaseUrls(_settings.KnownApiBaseUrls);
        MergeKnownApiBaseUrls(ResolveBundledFallbackApiBaseUrls());
        var preferredRegionalApiBaseUrl = ResolvePreferredApiRegionApiBaseUrl();
        if (!string.IsNullOrWhiteSpace(preferredRegionalApiBaseUrl))
        {
            ApiBaseUrl = preferredRegionalApiBaseUrl;
        }
        else if (!string.IsNullOrWhiteSpace(configuredApiBaseUrl))
        {
            ApiBaseUrl = configuredApiBaseUrl;
        }
        else
        {
            ApiBaseUrl = NormalizeBaseUrl(_settings.ApiBaseUrl);
        }
        MergeKnownApiBaseUrls([ApiBaseUrl]);
        InstallDirectory = string.IsNullOrWhiteSpace(_settings.InstallDirectory)
            ? _settingsService.GetDefaultInstallDirectory()
            : _settings.InstallDirectory;
        DebugMode = _settings.DebugMode;
        RamMb = _settings.RamMb;
        JavaMode = NormalizeJavaMode(_settings.JavaMode);
        SyncSelectedJavaModeOption();
        PlayerUsername = _settings.LastPlayerUsername;
        ResetTwoFactorState();
        AuthStatusText = T("status.notLoggedIn");
        LoadStoredPlayerSessionState();
        await RefreshAsync();
        await TryRestorePlayerSessionAsync();
        await TryApplyAutomaticUpdateAsync();
        StatusText = T("status.ready");
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        LoginCommand.NotifyCanExecuteChanged();
        SwitchAccountCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
        AddAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
        ToggleSettingsCommand.NotifyCanExecuteChanged();
        SelectRuApiRegionCommand.NotifyCanExecuteChanged();
        SelectEuApiRegionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedServerChanged(ManagedServerItem? value)
    {
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        RebuildRouteOptions();
        RefreshVisibleNewsItems();

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
        OnPropertyChanged(nameof(SelectedNewsMeta));
        OnPropertyChanged(nameof(SelectedNewsBody));
        OnPropertyChanged(nameof(SelectedNewsHeadlineText));
        OnPropertyChanged(nameof(SelectedNewsBodyText));
        OnPropertyChanged(nameof(SelectedNewsMetaText));
        OnPropertyChanged(nameof(HasSelectedNews));
        OnPropertyChanged(nameof(HasSelectedNewsPlaceholder));
    }

    partial void OnPlayerLoggedInAsChanged(string value)
    {
        NotifyAccountPresentationChanged();
    }

    partial void OnAuthStatusTextChanged(string value)
    {
        NotifyAccountPresentationChanged();
        NotifyLauncherHeaderPresentationChanged();
    }

    partial void OnStatusTextChanged(string value)
    {
        NotifyLauncherHeaderPresentationChanged();
    }

    partial void OnIsTwoFactorStepActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(TwoFactorHintText));
    }

    partial void OnPlayerTwoFactorCodeChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var normalized = NormalizeTwoFactorCode(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            PlayerTwoFactorCode = normalized;
        }
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

    partial void OnFileSyncProgressPercentChanged(int value)
    {
        OnPropertyChanged(nameof(FileSyncPercentText));
    }

    partial void OnFileSyncProcessedFilesChanged(int value)
    {
        OnPropertyChanged(nameof(FileSyncSummaryText));
    }

    partial void OnFileSyncTotalFilesChanged(int value)
    {
        OnPropertyChanged(nameof(FileSyncSummaryText));
    }

    partial void OnFileSyncDownloadedFilesChanged(int value)
    {
        OnPropertyChanged(nameof(FileSyncSummaryText));
    }

    partial void OnFileSyncVerifiedFilesChanged(int value)
    {
        OnPropertyChanged(nameof(FileSyncSummaryText));
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

    partial void OnPreferredApiRegionChanged(string value)
    {
        PreferredApiRegion = NormalizeApiRegionCode(value);
        NotifyApiRegionSelectionPresentationChanged();
    }

    partial void OnIsGameSessionActiveChanged(bool value)
    {
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
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
        RelocalizeServerOnlineStatuses();

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

    partial void OnSelectedStoredAccountChanged(StoredPlayerAccount? value)
    {
        SwitchAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        NotifyAccountPresentationChanged();
        if (!IsPlayerLoggedIn)
        {
            PlayerUsername = value?.Username?.Trim() ?? string.Empty;
            PlayerPassword = string.Empty;
            ResetTwoFactorState();
        }
    }

    private void RelocalizeServerOnlineStatuses()
    {
        foreach (var server in ManagedServers)
        {
            if (!IsPlayerLoggedIn)
            {
                server.OnlineStatusText = _languageCode == "en" ? "Monitoring disabled" : "Мониторинг выключен";
                server.OnlineStatusBrush = new SolidColorBrush(Color.Parse("#8799B5"));
                continue;
            }

            if (server.OnlineLastCheckedAtUtc == default)
            {
                server.OnlineStatusText = _languageCode == "en" ? "Checking..." : "Проверка...";
                server.OnlineStatusBrush = new SolidColorBrush(Color.Parse("#8EA3C0"));
                continue;
            }

            var result = new ServerOnlineProbeResult(
                server.ServerId,
                server.IsOnline,
                server.OnlinePlayers,
                server.OnlineMaxPlayers);
            server.OnlineStatusText = BuildOnlineStatusText(result);
            server.OnlineStatusBrush = server.IsOnline
                ? new SolidColorBrush(Color.Parse("#33B97C"))
                : new SolidColorBrush(Color.Parse("#D76464"));
        }
    }

    private bool CanOperate() => !IsBusy;

    private bool CanVerifyOrLaunch() => !IsBusy && !IsGameSessionActive && IsPlayerLoggedIn && SelectedServer is not null;

    private bool CanSelectRuApiRegion()
    {
        return !IsBusy &&
               HasApiRegionChoice &&
               !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion("ru")) &&
               !IsRuApiRegionSelected;
    }

    private bool CanSelectEuApiRegion()
    {
        return !IsBusy &&
               HasApiRegionChoice &&
               !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion("eu")) &&
               !IsEuApiRegionSelected;
    }

    private bool CanSwitchAccount()
    {
        return !IsBusy &&
               IsPlayerLoggedIn &&
               SelectedStoredAccount is not null &&
               !string.IsNullOrWhiteSpace(SelectedStoredAccount.AuthToken) &&
               !IsStoredAccountCurrentlyActive(SelectedStoredAccount);
    }

    private bool CanLogout()
    {
        return !IsBusy && IsPlayerLoggedIn;
    }

    private bool CanAddAccount()
    {
        return !IsBusy && IsPlayerLoggedIn;
    }

    private bool CanDeleteAccount()
    {
        return !IsBusy &&
               SelectedStoredAccount is not null &&
               !string.IsNullOrWhiteSpace(SelectedStoredAccount.Username);
    }

    private void StartFileSyncProgress()
    {
        IsFileSyncInProgress = true;
        FileSyncStageText = "Syncing client files...";
        FileSyncCurrentFileText = string.Empty;
        FileSyncProgressPercent = 0;
        FileSyncProcessedFiles = 0;
        FileSyncTotalFiles = 0;
        FileSyncDownloadedFiles = 0;
        FileSyncVerifiedFiles = 0;
    }

    private void UpdateFileSyncProgress(InstallProgressInfo info)
    {
        FileSyncProcessedFiles = Math.Max(0, info.ProcessedFiles);
        FileSyncTotalFiles = Math.Max(0, info.TotalFiles);
        FileSyncDownloadedFiles = Math.Max(0, info.DownloadedFiles);
        FileSyncVerifiedFiles = Math.Max(0, info.VerifiedFiles);
        FileSyncCurrentFileText = string.IsNullOrWhiteSpace(info.CurrentFilePath) ? info.Message : info.CurrentFilePath;

        if (FileSyncTotalFiles > 0)
        {
            FileSyncProgressPercent = (int)Math.Clamp(
                (double)FileSyncProcessedFiles / FileSyncTotalFiles * 100d,
                0d,
                100d);
        }
        else
        {
            FileSyncProgressPercent = 0;
        }
    }

    private void CompleteFileSyncProgress(InstallResult result)
    {
        FileSyncProcessedFiles = result.DownloadedFiles + result.VerifiedFiles;
        FileSyncTotalFiles = FileSyncProcessedFiles;
        FileSyncDownloadedFiles = result.DownloadedFiles;
        FileSyncVerifiedFiles = result.VerifiedFiles;
        FileSyncProgressPercent = 100;
        FileSyncStageText = "Client files are ready.";
        FileSyncCurrentFileText = string.Empty;
    }

    private void StopFileSyncProgress()
    {
        IsFileSyncInProgress = false;
    }

    private string BuildServerMonitoringText()
    {
        if (!IsPlayerLoggedIn)
        {
            return _languageCode == "en" ? "offline" : "оффлайн";
        }

        if (_lastServerOnlineRefreshUtc == default)
        {
            return "...";
        }

        return _lastServerOnlineRefreshUtc.ToLocalTime().ToString("HH:mm");
    }

    private string BuildAccountPanelSubtitle()
    {
        if (!IsPlayerLoggedIn)
        {
            return AuthStatusText;
        }

        return string.Empty;
    }

    private string BuildAccountPanelTitle()
    {
        if (HasMultipleStoredAccounts)
        {
            return _languageCode == "en" ? "Accounts" : "Аккаунты";
        }

        return !string.IsNullOrWhiteSpace(PlayerLoggedInAs)
            ? PlayerLoggedInAs
            : (SelectedStoredAccount?.Username?.Trim() ?? ProductName);
    }

    private string BuildLauncherHeaderStatusText()
    {
        var status = StatusText.Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return string.Empty;
        }

        if (!IsLauncherReady)
        {
            return status;
        }

        if (IsBusy || IsFileSyncInProgress)
        {
            return status;
        }

        if (string.Equals(status, T("status.ready"), StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (ManagedServers.Count > 0 &&
            string.Equals(status, F("status.loadedServers", ManagedServers.Count), StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (string.Equals(status, AuthStatusText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return status;
    }

    private async Task RefreshServerOnlineStatusesAsync(bool force = false)
    {
        if (!IsPlayerLoggedIn || ManagedServers.Count == 0)
        {
            return;
        }

        if (!force && _lastServerOnlineRefreshUtc != default &&
            DateTime.UtcNow - _lastServerOnlineRefreshUtc < TimeSpan.FromSeconds(3))
        {
            return;
        }

        if (!await _serverOnlineRefreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            foreach (var server in ManagedServers.Where(x => x.OnlineLastCheckedAtUtc == default))
            {
                server.OnlineStatusText = _languageCode == "en" ? "Checking..." : "Проверка...";
                server.OnlineStatusBrush = new SolidColorBrush(Color.Parse("#8EA3C0"));
            }

            var snapshot = ManagedServers.ToList();
            var tasks = snapshot.Select(server => ProbeServerOnlineAsync(server));
            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                var server = ManagedServers.FirstOrDefault(x => x.ServerId == result.ServerId);
                if (server is null)
                {
                    continue;
                }

                server.IsOnline = result.IsOnline;
                server.OnlinePlayers = result.OnlinePlayers;
                server.OnlineMaxPlayers = result.OnlineMaxPlayers;
                server.OnlineLastCheckedAtUtc = DateTime.UtcNow;
                server.OnlineStatusText = BuildOnlineStatusText(result);
                server.OnlineStatusBrush = result.IsOnline
                    ? new SolidColorBrush(Color.Parse("#33B97C"))
                    : new SolidColorBrush(Color.Parse("#D76464"));
            }

            _lastServerOnlineRefreshUtc = DateTime.UtcNow;
            OnPropertyChanged(nameof(ServerMonitoringText));
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to refresh server monitoring: {ex.Message}");
        }
        finally
        {
            _serverOnlineRefreshLock.Release();
        }
    }

    private string BuildOnlineStatusText(ServerOnlineProbeResult result)
    {
        if (!result.IsOnline)
        {
            return _languageCode == "en" ? "Offline" : "Оффлайн";
        }

        if (result.OnlineMaxPlayers > 0)
        {
            return _languageCode == "en"
                ? $"{result.OnlinePlayers}/{result.OnlineMaxPlayers} online"
                : $"{result.OnlinePlayers}/{result.OnlineMaxPlayers} онлайн";
        }

        return _languageCode == "en"
            ? $"{result.OnlinePlayers} online"
            : $"{result.OnlinePlayers} онлайн";
    }

    private async Task<ServerOnlineProbeResult> ProbeServerOnlineAsync(ManagedServerItem server)
    {
        var endpoints = ResolveProbeEndpoints(server);
        if (endpoints.Count == 0)
        {
            return new ServerOnlineProbeResult(server.ServerId, false, 0, -1);
        }

        var preferLegacyProbe = ShouldPreferLegacyProbe(server.McVersion);
        foreach (var endpoint in endpoints)
        {
            if (!preferLegacyProbe)
            {
                try
                {
                    var modernResult = await TryProbeModernServerAsync(endpoint.Host, endpoint.Port);
                    if (modernResult.IsOnline)
                    {
                        return modernResult with { ServerId = server.ServerId };
                    }
                }
                catch
                {
                }
            }

            try
            {
                var legacyResult = await TryProbeLegacyServerAsync(endpoint.Host, endpoint.Port);
                if (legacyResult.IsOnline)
                {
                    return legacyResult with { ServerId = server.ServerId };
                }
            }
            catch
            {
            }
        }

        return new ServerOnlineProbeResult(server.ServerId, false, 0, -1);
    }

    private static async Task<ServerOnlineProbeResult> TryProbeModernServerAsync(string host, int port)
    {
        using var cancellationTokenSource = new CancellationTokenSource(ServerOnlineTimeoutMs);
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationTokenSource.Token);
        using var stream = client.GetStream();

        using var handshakePayload = new MemoryStream();
        WriteVarInt(handshakePayload, 0);
        WriteVarInt(handshakePayload, 760);
        WriteString(handshakePayload, host);
        var portBytes = BitConverter.GetBytes((ushort)port);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(portBytes);
        }

        await handshakePayload.WriteAsync(portBytes, cancellationTokenSource.Token);
        WriteVarInt(handshakePayload, 1);

        using var handshakePacket = new MemoryStream();
        WriteVarInt(handshakePacket, checked((int)handshakePayload.Length));
        handshakePayload.Position = 0;
        await handshakePayload.CopyToAsync(handshakePacket, cancellationTokenSource.Token);
        handshakePacket.Position = 0;
        await handshakePacket.CopyToAsync(stream, cancellationTokenSource.Token);

        await stream.WriteAsync(new byte[] { 0x01, 0x00 }, cancellationTokenSource.Token);
        await stream.FlushAsync(cancellationTokenSource.Token);

        _ = await ReadVarIntAsync(stream, cancellationTokenSource.Token);
        var packetId = await ReadVarIntAsync(stream, cancellationTokenSource.Token);
        if (packetId != 0)
        {
            return new ServerOnlineProbeResult(Guid.Empty, false, 0, -1);
        }

        var jsonLength = await ReadVarIntAsync(stream, cancellationTokenSource.Token);
        if (jsonLength <= 0)
        {
            return new ServerOnlineProbeResult(Guid.Empty, false, 0, -1);
        }

        var jsonBytes = new byte[jsonLength];
        await stream.ReadExactlyAsync(jsonBytes, cancellationTokenSource.Token);
        var payloadJson = Encoding.UTF8.GetString(jsonBytes);
        var payload = JsonSerializer.Deserialize<MinecraftStatusPayload>(payloadJson);
        if (payload?.Players is null)
        {
            return new ServerOnlineProbeResult(Guid.Empty, true, 0, -1);
        }

        return new ServerOnlineProbeResult(
            Guid.Empty,
            true,
            Math.Max(0, payload.Players.Online),
            Math.Max(payload.Players.Max, -1));
    }

    private static async Task<ServerOnlineProbeResult> TryProbeLegacyServerAsync(string host, int port)
    {
        try
        {
            var extended = await TryProbeLegacyServerAsync(host, port, new byte[] { 0xFE, 0x01 });
            if (extended.IsOnline)
            {
                return extended;
            }
        }
        catch
        {
        }

        return await TryProbeLegacyServerAsync(host, port, new byte[] { 0xFE });
    }

    private static async Task<ServerOnlineProbeResult> TryProbeLegacyServerAsync(
        string host,
        int port,
        byte[] requestPayload)
    {
        using var cancellationTokenSource = new CancellationTokenSource(ServerOnlineTimeoutMs);
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationTokenSource.Token);
        using var stream = client.GetStream();

        await stream.WriteAsync(requestPayload, cancellationTokenSource.Token);
        await stream.FlushAsync(cancellationTokenSource.Token);

        var packetId = await ReadByteAsync(stream, cancellationTokenSource.Token);
        if (packetId != 0xFF)
        {
            return new ServerOnlineProbeResult(Guid.Empty, false, 0, -1);
        }

        var lengthBytes = new byte[2];
        await stream.ReadExactlyAsync(lengthBytes, cancellationTokenSource.Token);
        var charLength = BinaryPrimitives.ReadInt16BigEndian(lengthBytes);
        if (charLength <= 0)
        {
            return new ServerOnlineProbeResult(Guid.Empty, true, 0, -1);
        }

        var payloadBytes = new byte[charLength * 2];
        await stream.ReadExactlyAsync(payloadBytes, cancellationTokenSource.Token);
        var responseText = Encoding.BigEndianUnicode.GetString(payloadBytes);
        var parts = responseText.Split('\0');
        if (parts.Length >= 6 &&
            string.Equals(parts[0], "§1", StringComparison.Ordinal) &&
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var online) &&
            int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
        {
            return new ServerOnlineProbeResult(Guid.Empty, true, Math.Max(0, online), Math.Max(max, -1));
        }

        // Older server list ping format is MOTD§online§max.
        var sectionParts = responseText.Split('§');
        if (sectionParts.Length >= 3 &&
            int.TryParse(sectionParts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var oldOnline) &&
            int.TryParse(sectionParts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var oldMax))
        {
            return new ServerOnlineProbeResult(Guid.Empty, true, Math.Max(0, oldOnline), Math.Max(oldMax, -1));
        }

        return new ServerOnlineProbeResult(Guid.Empty, true, 0, -1);
    }

    private static bool ShouldPreferLegacyProbe(string? mcVersion)
    {
        if (!TryParseMinecraftMajorMinor(mcVersion, out var major, out var minor))
        {
            return false;
        }

        return major == 1 && minor <= 6;
    }

    private static bool TryParseMinecraftMajorMinor(string? mcVersion, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(mcVersion))
        {
            return false;
        }

        var raw = mcVersion.Trim();
        var match = Regex.Match(raw, @"(?<!\d)(\d+)\.(\d+)(?:\.\d+)?", RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups.Count < 3)
        {
            return false;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out major) &&
               int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
    }

    private static List<ServerEndpoint> ResolveProbeEndpoints(ManagedServerItem server)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new List<ServerEndpoint>(3);

        Add(server.MainAddress, server.MainPort);
        Add(server.Address, server.Port);
        Add(server.RuProxyAddress, server.RuProxyPort);
        return endpoints;

        void Add(string? rawAddress, int rawPort)
        {
            if (!TryResolveServerEndpoint(rawAddress, rawPort, out var endpoint))
            {
                return;
            }

            var key = $"{endpoint.Host}:{endpoint.Port}";
            if (!seen.Add(key))
            {
                return;
            }

            endpoints.Add(endpoint);
        }
    }

    private static bool TryResolveServerEndpoint(string? rawAddress, int rawPort, out ServerEndpoint endpoint)
    {
        endpoint = default;
        var trimmed = (rawAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var normalized = trimmed;
        if (normalized.StartsWith("minecraft://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["minecraft://".Length..];
        }
        else if (normalized.StartsWith("mc://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["mc://".Length..];
        }

        var resolvedHost = normalized.Trim();
        var resolvedPort = rawPort is > 0 and <= 65535 ? rawPort : 25565;

        if (resolvedHost.StartsWith("[", StringComparison.Ordinal) &&
            resolvedHost.Contains(']', StringComparison.Ordinal))
        {
            var closingBracketIndex = resolvedHost.IndexOf(']');
            var bracketHost = resolvedHost[1..closingBracketIndex].Trim();
            var remaining = resolvedHost[(closingBracketIndex + 1)..].Trim();
            if (remaining.StartsWith(":", StringComparison.Ordinal) &&
                int.TryParse(remaining[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bracketPort) &&
                bracketPort is > 0 and <= 65535)
            {
                resolvedPort = bracketPort;
            }

            resolvedHost = bracketHost;
        }
        else
        {
            var slashIndex = resolvedHost.IndexOf('/');
            if (slashIndex >= 0)
            {
                resolvedHost = resolvedHost[..slashIndex];
            }

            var lastColon = resolvedHost.LastIndexOf(':');
            if (lastColon > 0 &&
                lastColon < resolvedHost.Length - 1 &&
                resolvedHost.Count(ch => ch == ':') == 1 &&
                int.TryParse(
                    resolvedHost[(lastColon + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var inlinePort) &&
                inlinePort is > 0 and <= 65535)
            {
                resolvedPort = inlinePort;
                resolvedHost = resolvedHost[..lastColon];
            }
        }

        resolvedHost = resolvedHost.Trim();
        if (string.IsNullOrWhiteSpace(resolvedHost))
        {
            return false;
        }

        endpoint = new ServerEndpoint(resolvedHost, resolvedPort);
        return true;
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var unsigned = (uint)value;
        do
        {
            var temp = (byte)(unsigned & 0b0111_1111u);
            unsigned >>= 7;
            if (unsigned != 0)
            {
                temp |= 0b1000_0000;
            }

            stream.WriteByte(temp);
        } while (unsigned != 0);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var numRead = 0;
        var result = 0;

        while (true)
        {
            var read = await ReadByteAsync(stream, cancellationToken);
            var value = read & 0b0111_1111;
            result |= value << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                throw new InvalidOperationException("VarInt is too big.");
            }

            if ((read & 0b1000_0000) == 0)
            {
                break;
            }
        }

        return result;
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        if (read != 1)
        {
            throw new IOException("Unexpected end of stream.");
        }

        return buffer[0];
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(RefreshCoreAsync);
    }

    private async Task RefreshCoreAsync()
    {
        StatusText = T("status.fetchingBootstrap");
        var bootstrap = await ExecuteAgainstApiFailoverAsync(
            candidate => _launcherApiService.GetBootstrapAsync(
                candidate,
                _playerAuthToken,
                _playerAuthTokenType));
        var bootstrapApiBaseUrl = NormalizeBaseUrlOrEmpty(bootstrap.PublicBaseUrl);
        if (!string.IsNullOrWhiteSpace(bootstrapApiBaseUrl))
        {
            MergeKnownApiBaseUrls([bootstrapApiBaseUrl]);
            if (string.IsNullOrWhiteSpace(ResolvePreferredApiRegionApiBaseUrl()))
            {
                ApiBaseUrl = bootstrapApiBaseUrl;
            }
        }
        MergeKnownApiBaseUrls(bootstrap.FallbackApiBaseUrls);
        var assetRefreshVersion = Interlocked.Increment(ref _assetRefreshVersion);
        _ = FlushPendingSubmissionsAsync();
        _ = TrySubmitInstallTelemetryAsync(bootstrap);

        await ApplyLauncherDirectoryNameAsync(bootstrap.Branding);
        ApplyBranding(ApiBaseUrl, bootstrap.Branding, assetRefreshVersion);
        var discordRpcEnabled = bootstrap.Constraints.DiscordRpcEnabled;
        var discordRpcPrivacyMode = bootstrap.Constraints.DiscordRpcPrivacyMode;
        _discordRpcService.ConfigurePolicy(discordRpcEnabled, discordRpcPrivacyMode, ProductName);
        ApplyLauncherUpdateInfo(bootstrap.LauncherUpdate);

        if (IsPlayerLoggedIn && !string.IsNullOrWhiteSpace(_playerAuthToken))
        {
            await TryRefreshCurrentPlayerSessionStateAsync();
        }

        var totalMemoryMb = GetTotalAvailableMemoryMb();
        RamMinMb = Math.Max(bootstrap.Constraints.MinRamMb, 512);
        RamMaxMb = Math.Max(RamMinMb, totalMemoryMb - Math.Max(bootstrap.Constraints.ReservedSystemRamMb, 512));
        RamMb = Math.Clamp(RamMb, RamMinMb, RamMaxMb);

        var allServers = new List<ManagedServerItem>();
        var missingServerIconCandidates = new List<(Guid ServerId, string ServerIconUrl, string ProfileIconUrl)>();
        var orderedProfiles = bootstrap.Profiles.OrderBy(profile => profile.Priority);
        _profileBundledRuntimeKeys.Clear();
        foreach (var profile in orderedProfiles)
        {
            var normalizedBundledRuntimeKey = (profile.BundledRuntimeKey ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(profile.Slug))
            {
                _profileBundledRuntimeKeys[profile.Slug.Trim()] = normalizedBundledRuntimeKey;
            }

            var orderedServers = profile.Servers.OrderBy(server => server.Order);
            foreach (var server in orderedServers)
            {
                var rpc = server.DiscordRpc ?? profile.DiscordRpc;
                var effectiveRpcEnabled = (rpc?.Enabled ?? false) && discordRpcEnabled;
                var effectiveRpcDetails = discordRpcPrivacyMode ? string.Empty : (rpc?.DetailsText ?? string.Empty);
                var effectiveRpcState = discordRpcPrivacyMode ? string.Empty : (rpc?.StateText ?? string.Empty);
                var effectiveRpcLargeText = discordRpcPrivacyMode ? string.Empty : (rpc?.LargeImageText ?? string.Empty);
                var effectiveRpcSmallText = discordRpcPrivacyMode ? string.Empty : (rpc?.SmallImageText ?? string.Empty);
                var icon = TryResolveServerIconFromCache(ApiBaseUrl, server.IconUrl, profile.IconUrl);
                if (icon is null)
                {
                    missingServerIconCandidates.Add((server.Id, server.IconUrl, profile.IconUrl));
                }

                allServers.Add(new ManagedServerItem
                {
                    ServerId = server.Id,
                    ProfileId = profile.Id,
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
                    Icon = icon,
                    IsOnline = false,
                    OnlinePlayers = 0,
                    OnlineMaxPlayers = -1,
                    OnlineStatusText = _languageCode == "en" ? "Checking..." : "Проверка...",
                    OnlineStatusBrush = new SolidColorBrush(Color.Parse("#8EA3C0")),
                    OnlineLastCheckedAtUtc = default
                });
            }
        }

        _allNewsItems.Clear();
        _allNewsItems.AddRange(bootstrap.News
            .OrderByDescending(item => item.Pinned)
            .ThenByDescending(item => item.CreatedAtUtc)
            .Select(item => new LauncherNewsItem
            {
                Id = item.Id,
                Title = item.Title,
                Body = item.Body,
                Preview = BuildNewsPreview(item.Body),
                Source = item.Source,
                ScopeType = NormalizeNewsScopeType(item.ScopeType),
                ScopeId = NormalizeNewsScopeId(item.ScopeId),
                ScopeName = (item.ScopeName ?? string.Empty).Trim(),
                Pinned = item.Pinned,
                CreatedAtUtc = item.CreatedAtUtc,
                Meta = BuildNewsMeta(item.Source, item.Pinned, item.CreatedAtUtc, item.ScopeType, item.ScopeName)
            })
            .ToList());

        ManagedServers.Clear();
        foreach (var server in allServers)
        {
            ManagedServers.Add(server);
        }

        RefreshVisibleNewsItems();

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
        NotifyLauncherHeaderPresentationChanged();
        NotifyAccountPresentationChanged();
        QueueServerIconRefresh(ApiBaseUrl, assetRefreshVersion, missingServerIconCandidates);

        if (IsPlayerLoggedIn)
        {
            await RefreshServerOnlineStatusesAsync(force: true);
        }
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
            StartFileSyncProgress();
            try
            {
                StatusText = T("status.fetchingManifest");
                var manifest = await _launcherApiService.GetManifestAsync(
                    ApiBaseUrl,
                    SelectedServer.ProfileSlug,
                    _playerAuthToken,
                    _playerAuthTokenType);
                ApplyProfileRuntimeFallback(manifest, SelectedServer.ProfileSlug);
                StatusText = _languageCode == "en" ? "Preparing files..." : "Подготовка файлов...";

                var progress = new Progress<InstallProgressInfo>(info =>
                {
                    UpdateFileSyncProgress(info);
                    var currentPath = string.IsNullOrWhiteSpace(info.CurrentFilePath) ? info.Message : info.CurrentFilePath;
                    StatusText = F("status.verifyingProgress", info.ProcessedFiles, info.TotalFiles, currentPath);
                });

                var result = await Task.Run(() =>
                        _manifestInstallerService.VerifyAndInstallAsync(
                            ApiBaseUrl,
                            manifest,
                            InstallDirectory,
                            progress),
                    CancellationToken.None);

                CompleteFileSyncProgress(result);
                var verifyCompleteText = F("status.verifyComplete", result.DownloadedFiles, result.VerifiedFiles);
                StatusText = result.RemovedFiles > 0
                    ? $"{verifyCompleteText} Removed orphan files: {result.RemovedFiles}."
                    : verifyCompleteText;
                _logService.LogInfo(StatusText);
            }
            finally
            {
                StopFileSyncProgress();
            }
        });
    }

    private async Task LaunchAsync()
    {
        if (SelectedServer is null || IsBusy || IsGameSessionActive)
        {
            return;
        }

        HasCrash = false;
        CrashSummary = string.Empty;

        ManagedServerItem? selectedServer = null;
        LauncherManifest? manifest = null;
        GameLaunchRoute? launchRoute = null;
        LaunchResult? launchResult = null;
        Exception? launchException = null;
        var occurredAtUtc = DateTime.UtcNow;
        var busyReleasedForGameSession = false;

        EnterBusyOperation();
        try
        {
            var preLaunchAccountSwitchError = await EnsureSelectedStoredAccountReadyForLaunchAsync();
            if (!string.IsNullOrWhiteSpace(preLaunchAccountSwitchError))
            {
                StatusText = preLaunchAccountSwitchError;
                _logService.LogInfo(preLaunchAccountSwitchError);
                return;
            }

            var preLaunchSessionValidationError = await ValidatePlayerSessionBeforeLaunchAsync();
            if (!string.IsNullOrWhiteSpace(preLaunchSessionValidationError))
            {
                StatusText = preLaunchSessionValidationError;
                _logService.LogInfo(preLaunchSessionValidationError);
                return;
            }

            selectedServer = SelectedServer;
            if (selectedServer is null)
            {
                StatusText = T("status.noServers");
                return;
            }

            await SaveSettingsAsync();

            StatusText = T("status.fetchingManifest");
            manifest = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.GetManifestAsync(
                    candidate,
                    selectedServer.ProfileSlug,
                    _playerAuthToken,
                    _playerAuthTokenType));
            ApplyProfileRuntimeFallback(manifest, selectedServer.ProfileSlug);
            StatusText = _languageCode == "en" ? "Preparing files..." : "Подготовка файлов...";

            StartFileSyncProgress();
            var progress = new Progress<InstallProgressInfo>(info =>
            {
                UpdateFileSyncProgress(info);
                var currentPath = string.IsNullOrWhiteSpace(info.CurrentFilePath) ? info.Message : info.CurrentFilePath;
                StatusText = F("status.verifyingProgress", info.ProcessedFiles, info.TotalFiles, currentPath);
            });

            var installResult = await Task.Run(() =>
                    _manifestInstallerService.VerifyAndInstallAsync(
                        ApiBaseUrl,
                        manifest,
                        InstallDirectory,
                        progress),
                CancellationToken.None);
            CompleteFileSyncProgress(installResult);
            StopFileSyncProgress();
            if (installResult.RemovedFiles > 0)
            {
                _logService.LogInfo($"Removed orphan files: {installResult.RemovedFiles}");
            }

            StatusText = T("status.launchingJava");
            launchRoute = ResolveLaunchRoute(selectedServer);
            var gameSession = await TryStartGameSessionAsync(selectedServer);
            if (gameSession is null)
            {
                return;
            }

            using var gameSessionHeartbeatCts = new CancellationTokenSource();
            var heartbeatTask = RunGameSessionHeartbeatLoopAsync(gameSession, gameSessionHeartbeatCts.Token);
            _discordRpcService.SetLaunchingPresence(selectedServer);

            IsGameSessionActive = true;
            ExitBusyOperation();
            busyReleasedForGameSession = true;

            try
            {
                _discordRpcService.SetInGamePresence(selectedServer);
                launchResult = await Task.Run(() =>
                        _gameLaunchService.LaunchAsync(
                            manifest,
                            BuildSettingsSnapshot(includeRuntimeAuthSnapshot: true),
                            launchRoute,
                            installResult.InstanceDirectory,
                            line => _logService.LogInfo(line)),
                    CancellationToken.None);
            }
            finally
            {
                IsGameSessionActive = false;
                gameSessionHeartbeatCts.Cancel();
                try
                {
                    await heartbeatTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logService.LogInfo($"Game session heartbeat stopped with warning: {ex.Message}");
                }

                await TryStopGameSessionAsync(gameSession.SessionId);
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
            StopFileSyncProgress();
            IsGameSessionActive = false;
            _logService.LogError(ex.ToString());
        }
        finally
        {
            if (!busyReleasedForGameSession)
            {
                ExitBusyOperation();
            }
        }

        HasCrash = true;
        var fullLogExcerpt = string.Join(Environment.NewLine, _logService.GetRecentLines(120));
        var crashLines = _logService.GetRecentLines(60);
        CrashSummary = string.Join(Environment.NewLine, crashLines);

        var reason = BuildCrashReason(launchResult?.ExitCode, launchException);
        StatusText = launchResult is not null
            ? F("status.gameExitedCode", launchResult.ExitCode)
            : F("status.error", reason);

        var crashServer = selectedServer ?? SelectedServer;
        if (crashServer is null)
        {
            return;
        }

        var crashId = await TrySubmitCrashReportAsync(
            crashServer,
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
        using var flushCts = new CancellationTokenSource(PendingSubmissionFlushTimeout);
        PendingSubmissionFlushResult result;

        try
        {
            result = await _pendingSubmissionService.FlushAsync(async (item, cancellationToken) =>
            {
                using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendCts.CancelAfter(PendingSubmissionSendTimeout);
                var sendToken = sendCts.Token;

                if (item.Type.Equals(PendingSubmissionTypes.CrashReport, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.CrashReport is null)
                    {
                        return true;
                    }

                    var response = await _launcherApiService.SubmitCrashReportAsync(
                        item.ApiBaseUrl,
                        item.CrashReport,
                        sendToken);
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
                        sendToken);

                    if (response.Accepted || !response.Enabled)
                    {
                        _installTelemetrySent = true;
                    }

                    return true;
                }

                return true;
            }, flushCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logService.LogInfo("Pending submissions sync timed out and was deferred.");
            return;
        }

        if (result.SentCount == 0 && result.DroppedCount == 0 && result.FailedCount == 0)
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

    private async Task SelectApiRegionAsync(string regionCode)
    {
        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        if (string.IsNullOrWhiteSpace(normalizedRegionCode))
        {
            return;
        }

        var preferredApiBaseUrl = ResolveConfiguredApiBaseUrlForRegion(normalizedRegionCode);
        if (string.IsNullOrWhiteSpace(preferredApiBaseUrl))
        {
            return;
        }

        PreferredApiRegion = normalizedRegionCode;
        ApiBaseUrl = preferredApiBaseUrl;
        MergeKnownApiBaseUrls([preferredApiBaseUrl]);
        await PersistSettingsSnapshotAsync();
    }

    private string ResolveInitialApiRegion(string? storedRegionCode)
    {
        var normalizedStoredRegionCode = NormalizeApiRegionCode(storedRegionCode);
        if (!string.IsNullOrWhiteSpace(normalizedStoredRegionCode) &&
            !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion(normalizedStoredRegionCode)))
        {
            return normalizedStoredRegionCode;
        }

        var hasRuRegion = !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion("ru"));
        var hasEuRegion = !string.IsNullOrWhiteSpace(ResolveConfiguredApiBaseUrlForRegion("eu"));
        if (hasRuRegion)
        {
            return "ru";
        }

        return hasEuRegion ? "eu" : string.Empty;
    }

    private string ResolvePreferredApiRegionApiBaseUrl()
    {
        var resolvedRegionCode = ResolveInitialApiRegion(PreferredApiRegion);
        return string.IsNullOrWhiteSpace(resolvedRegionCode)
            ? string.Empty
            : ResolveConfiguredApiBaseUrlForRegion(resolvedRegionCode);
    }

    private static string NormalizeApiRegionCode(string? regionCode)
    {
        return (regionCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ru" => "ru",
            "eu" => "eu",
            _ => string.Empty
        };
    }

    private static string ResolveConfiguredApiBaseUrlForRegion(string regionCode)
    {
        var normalizedRegionCode = NormalizeApiRegionCode(regionCode);
        if (string.IsNullOrWhiteSpace(normalizedRegionCode))
        {
            return string.Empty;
        }

        var environmentVariableName = normalizedRegionCode == "ru"
            ? LauncherApiBaseUrlRuEnvVar
            : LauncherApiBaseUrlEuEnvVar;
        var assemblyMetadataKey = normalizedRegionCode == "ru"
            ? LauncherApiBaseUrlRuAssemblyMetadataKey
            : LauncherApiBaseUrlEuAssemblyMetadataKey;
        var environmentBaseUrl = NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable(environmentVariableName));
        if (!string.IsNullOrWhiteSpace(environmentBaseUrl))
        {
            return environmentBaseUrl;
        }

        return NormalizeBaseUrlOrEmpty(ResolveAssemblyMetadataValue(assemblyMetadataKey));
    }

    private static string? ResolveAssemblyMetadataValue(string key)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?
            .Value;
    }

    private void NotifyApiRegionSelectionPresentationChanged()
    {
        OnPropertyChanged(nameof(HasApiRegionChoice));
        OnPropertyChanged(nameof(ApiRegionHeaderText));
        OnPropertyChanged(nameof(ApiRegionHintText));
        OnPropertyChanged(nameof(ApiRegionRfButtonText));
        OnPropertyChanged(nameof(ApiRegionEuButtonText));
        OnPropertyChanged(nameof(ApiRegionRuToolTipText));
        OnPropertyChanged(nameof(ApiRegionEuToolTipText));
        OnPropertyChanged(nameof(IsRuApiRegionSelected));
        OnPropertyChanged(nameof(IsEuApiRegionSelected));
        OnPropertyChanged(nameof(ApiRegionRuBackgroundBrush));
        OnPropertyChanged(nameof(ApiRegionRuBorderBrush));
        OnPropertyChanged(nameof(ApiRegionRuForegroundBrush));
        OnPropertyChanged(nameof(ApiRegionEuBackgroundBrush));
        OnPropertyChanged(nameof(ApiRegionEuBorderBrush));
        OnPropertyChanged(nameof(ApiRegionEuForegroundBrush));
        SelectRuApiRegionCommand.NotifyCanExecuteChanged();
        SelectEuApiRegionCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveSettingsAsync()
    {
        await PersistSettingsSnapshotAsync(updateStatusText: true);
    }

    private async Task PersistSettingsSnapshotAsync(bool updateStatusText = false)
    {
        _settings = BuildSettingsSnapshot();
        await _settingsService.SaveAsync(_settings);

        if (updateStatusText)
        {
            StatusText = T("status.settingsSaved");
        }
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

    private LauncherSettings BuildSettingsSnapshot(bool includeRuntimeAuthSnapshot = false)
    {
        var configuredApiBaseUrl = TryResolveConfiguredApiBaseUrl();
        var persistedApiBaseUrl = string.IsNullOrWhiteSpace(configuredApiBaseUrl)
            ? NormalizeBaseUrl(ApiBaseUrl)
            : string.Empty;
        var storedAccounts = NormalizeStoredAccounts(StoredPlayerAccounts);
        var activeStoredAccount = ResolveStoredAccount(PlayerLoggedInAs)
            ?? SelectedStoredAccount;
        var hasActiveSession = IsPlayerLoggedIn &&
                               !string.IsNullOrWhiteSpace(_playerAuthToken) &&
                               !string.IsNullOrWhiteSpace(PlayerLoggedInAs);
        var canAutoRestore = _allowAutoSessionRestore && (hasActiveSession || activeStoredAccount is not null);
        var runtimeAuthToken = includeRuntimeAuthSnapshot && hasActiveSession
            ? _playerAuthToken
            : string.Empty;
        var runtimeAuthTokenType = includeRuntimeAuthSnapshot && hasActiveSession
            ? _playerAuthTokenType
            : "Bearer";
        var runtimeAuthUsername = includeRuntimeAuthSnapshot && hasActiveSession
            ? PlayerLoggedInAs.Trim()
            : string.Empty;
        var runtimeAuthExternalId = includeRuntimeAuthSnapshot && hasActiveSession
            ? _playerAuthExternalId
            : string.Empty;
        var runtimeAuthRoles = includeRuntimeAuthSnapshot && hasActiveSession
            ? NormalizePlayerRoles(_playerAuthRoles)
            : [];
        var runtimeAuthApiBaseUrl = includeRuntimeAuthSnapshot && hasActiveSession
            ? NormalizeBaseUrlOrEmpty(_playerAuthApiBaseUrl)
            : string.Empty;

        return new LauncherSettings
        {
            ApiBaseUrl = persistedApiBaseUrl,
            PreferredApiRegion = NormalizeApiRegionCode(PreferredApiRegion),
            InstallDirectory = InstallDirectory.Trim(),
            DebugMode = DebugMode,
            RamMb = RamMb,
            JavaMode = JavaMode,
            Language = _languageCode,
            KnownApiBaseUrls = [.. _knownApiBaseUrls
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            ProfileRouteSelections = _profileRouteSelections
                .Select(x => new ProfileRouteSelection
                {
                    ProfileSlug = x.Key,
                    Route = NormalizeRouteCode(x.Value)
                })
                .OrderBy(x => x.ProfileSlug, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SelectedServerId = SelectedServer?.ServerId.ToString() ?? string.Empty,
            LastPlayerUsername = PlayerUsername.Trim(),
            // Persist path keeps these empty; runtime launch snapshot may include active session values.
            PlayerAuthToken = runtimeAuthToken,
            PlayerAuthTokenType = runtimeAuthTokenType,
            PlayerAuthUsername = runtimeAuthUsername,
            PlayerAuthExternalId = runtimeAuthExternalId,
            PlayerAuthRoles = runtimeAuthRoles,
            PlayerAuthApiBaseUrl = NormalizePersistedApiBaseUrl(runtimeAuthApiBaseUrl, configuredApiBaseUrl),
            PlayerAccounts = [.. storedAccounts.Select(account => new StoredPlayerAccount
            {
                Username = account.Username,
                AuthToken = account.AuthToken,
                AuthTokenType = account.AuthTokenType,
                ExternalId = account.ExternalId,
                Roles = [.. account.Roles],
                ApiBaseUrl = NormalizePersistedApiBaseUrl(account.ApiBaseUrl, configuredApiBaseUrl),
                LastUsedAtUtc = account.LastUsedAtUtc
            })],
            ActivePlayerAccountUsername = canAutoRestore ? (activeStoredAccount?.Username ?? string.Empty) : string.Empty
        };
    }

    private async Task LoginAsync()
    {
        await RunBusyAsync(async () =>
        {
            ClearBanNotice();
            var enteredUsername = PlayerUsername.Trim();
            var enteredPassword = PlayerPassword;
            var usePendingCredentials = IsTwoFactorStepActive &&
                                        !string.IsNullOrWhiteSpace(_pendingTwoFactorUsername) &&
                                        !string.IsNullOrWhiteSpace(_pendingTwoFactorPassword);
            var username = usePendingCredentials ? _pendingTwoFactorUsername : enteredUsername;
            var password = usePendingCredentials ? _pendingTwoFactorPassword : enteredPassword;
            var normalizedTwoFactorCode = NormalizeTwoFactorCode(PlayerTwoFactorCode);
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException(T("validation.usernameRequired"));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(T("validation.passwordRequired"));
            }

            if (IsTwoFactorStepActive && string.IsNullOrWhiteSpace(normalizedTwoFactorCode))
            {
                throw new InvalidOperationException(T("status.twoFactorRequired"));
            }

            AuthStatusText = T("status.authorizing");
            StatusText = AuthStatusText;

            var response = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.LoginAsync(candidate, new PublicAuthLoginRequest
                {
                    Username = username,
                    Password = password,
                    HwidFingerprint = ComputeHwidFingerprint(),
                    DeviceUserName = ComputeDeviceUserName(),
                    TwoFactorCode = IsTwoFactorStepActive ? normalizedTwoFactorCode : string.Empty
                }));

            if (response.RequiresTwoFactor)
            {
                IsTwoFactorStepActive = true;
                TwoFactorSetupSecret = response.TwoFactorSecret.Trim();
                TwoFactorSetupUri = response.TwoFactorProvisioningUri.Trim();
                _pendingTwoFactorUsername = username;
                _pendingTwoFactorPassword = password;
                AuthStatusText = string.IsNullOrWhiteSpace(response.Message)
                    ? T("status.twoFactorRequired")
                    : response.Message.Trim();
                StatusText = AuthStatusText;
                return;
            }

            SetAuthenticatedPlayerSession(
                response.Token,
                response.TokenType,
                response.Username,
                response.ExternalId,
                response.Roles,
                ApiBaseUrl);

            await RefreshPlayerCosmeticsAsync(response.Username);
            await PersistSettingsSnapshotAsync();
            await RefreshCoreAsync();
            StatusText = T("status.ready");
            _logService.LogInfo($"Player login success: {response.Username} ({response.ExternalId})");
        });
    }

    private async Task SwitchAccountAsync()
    {
        if (SelectedStoredAccount is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var account = SelectedStoredAccount;
            var switchError = await ActivateStoredAccountAsync(account, refreshLauncherContent: true);
            if (!string.IsNullOrWhiteSpace(switchError))
            {
                StatusText = switchError;
                _logService.LogInfo(switchError);
                return;
            }

            StatusText = T("status.ready");
        });
    }

    private async Task LogoutAsync()
    {
        await RunBusyAsync(async () =>
        {
            ClearBanNotice();
            var tokenToRevoke = _playerAuthToken;
            var tokenTypeToRevoke = _playerAuthTokenType;

            if (!string.IsNullOrWhiteSpace(tokenToRevoke))
            {
                try
                {
                    await _launcherApiService.LogoutAsync(ApiBaseUrl, tokenToRevoke, tokenTypeToRevoke);
                }
                catch (Exception ex)
                {
                    _logService.LogInfo($"Server-side logout call failed (will continue local logout): {ex.Message}");
                }
            }

            _allowAutoSessionRestore = false;
            IsPlayerLoggedIn = false;
            PlayerPassword = string.Empty;
            ResetTwoFactorState();
            ClearAuthenticatedPlayerSession();
            StatusText = T("status.notLoggedIn");
            await PersistSettingsSnapshotAsync();
        });
    }

    private void AddAccount()
    {
        if (!CanAddAccount())
        {
            return;
        }

        ClearBanNotice();
        _allowAutoSessionRestore = false;
        IsPlayerLoggedIn = false;
        ResetTwoFactorState();
        ClearAuthenticatedPlayerSession();
        PlayerPassword = string.Empty;
        PlayerUsername = string.Empty;
        SetSelectedStoredAccount(null);
        StatusText = _languageCode == "en" ? "Add another account." : "Добавьте новый аккаунт.";
    }

    private async Task DeleteSelectedAccountAsync()
    {
        if (SelectedStoredAccount is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var selectedAccount = SelectedStoredAccount;
            if (selectedAccount is null)
            {
                return;
            }

            var username = selectedAccount.Username.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            var removingCurrentSessionAccount = string.Equals(
                PlayerLoggedInAs,
                username,
                StringComparison.OrdinalIgnoreCase);

            RemoveStoredAccount(username);

            if (removingCurrentSessionAccount)
            {
                _allowAutoSessionRestore = false;
                IsPlayerLoggedIn = false;
                PlayerPassword = string.Empty;
                ResetTwoFactorState();
                ClearAuthenticatedPlayerSession();
                await PersistSettingsSnapshotAsync();
                StatusText = _languageCode == "en"
                    ? "Selected account removed. Login again."
                    : "Выбранный аккаунт удалён. Войдите снова.";
                return;
            }

            await PersistSettingsSnapshotAsync();
            StatusText = _languageCode == "en"
                ? $"Account {username} removed."
                : $"Аккаунт {username} удалён.";
        });
    }

    private void LoadStoredPlayerSessionState()
    {
        var knownAccounts = NormalizeStoredAccounts(_settings.PlayerAccounts);
        SetStoredAccounts(knownAccounts);

        var activeStoredAccount = ResolveStoredAccount(_settings.ActivePlayerAccountUsername);
        SetSelectedStoredAccount(activeStoredAccount ?? StoredPlayerAccounts.FirstOrDefault());

        if (activeStoredAccount is null)
        {
            _allowAutoSessionRestore = false;
            ClearAuthenticatedPlayerSession();
            return;
        }

        _allowAutoSessionRestore = true;
        _playerAuthToken = (activeStoredAccount.AuthToken ?? string.Empty).Trim();
        _playerAuthTokenType = string.IsNullOrWhiteSpace(activeStoredAccount.AuthTokenType)
            ? "Bearer"
            : activeStoredAccount.AuthTokenType.Trim();
        _playerAuthExternalId = (activeStoredAccount.ExternalId ?? string.Empty).Trim();
        _playerAuthRoles = NormalizePlayerRoles(activeStoredAccount.Roles);
        _playerAuthApiBaseUrl = NormalizeBaseUrlOrEmpty(activeStoredAccount.ApiBaseUrl);
        PlayerUsername = activeStoredAccount.Username.Trim();
        NotifyAccountPresentationChanged();
    }

    private async Task TryRestorePlayerSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(_playerAuthToken))
        {
            return;
        }

        var fallbackStoredUsername = SelectedStoredAccount?.Username?.Trim() ?? string.Empty;

        try
        {
            var session = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.GetSessionAsync(candidate, _playerAuthToken, _playerAuthTokenType),
                preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl());
            SetAuthenticatedPlayerSession(
                _playerAuthToken,
                _playerAuthTokenType,
                session.Username,
                session.ExternalId,
                session.Roles,
                ApiBaseUrl);

            await RefreshPlayerCosmeticsAsync(session.Username);
            await PersistSettingsSnapshotAsync();
            _logService.LogInfo($"Player session restored: {session.Username} ({session.ExternalId})");
        }
        catch (LauncherApiException apiException) when (
            apiException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var isBanNotice = TryShowBanNotice(apiException);
            if (!string.IsNullOrWhiteSpace(fallbackStoredUsername))
            {
                RemoveStoredAccount(fallbackStoredUsername);
            }

            ClearAuthenticatedPlayerSession();
            await PersistSettingsSnapshotAsync();
            if (isBanNotice)
            {
                StatusText = BanNoticeMessage;
            }
            _logService.LogInfo("Stored player session was rejected by API. Login is required.");
        }
        catch (Exception ex)
        {
            _logService.LogError($"Player session restore failed: {ex.Message}");
        }
    }

    private string ResolvePreferredPlayerApiBaseUrl(string? storedPreferredApiBaseUrl = null)
    {
        var currentApiBaseUrl = NormalizeBaseUrlOrEmpty(ApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(currentApiBaseUrl))
        {
            return currentApiBaseUrl;
        }

        return NormalizeBaseUrlOrEmpty(string.IsNullOrWhiteSpace(storedPreferredApiBaseUrl)
            ? _playerAuthApiBaseUrl
            : storedPreferredApiBaseUrl);
    }

    private async Task<string> ValidatePlayerSessionBeforeLaunchAsync()
    {
        if (!IsPlayerLoggedIn)
        {
            return _languageCode == "en"
                ? "Login is required before launch."
                : "Перед запуском требуется выполнить вход.";
        }

        if (string.IsNullOrWhiteSpace(_playerAuthToken))
        {
            return _languageCode == "en"
                ? "Player auth token is missing. Login is required."
                : "Токен игрока отсутствует. Требуется повторный вход.";
        }

        try
        {
            var session = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.GetSessionAsync(candidate, _playerAuthToken, _playerAuthTokenType),
                preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl());
            SetAuthenticatedPlayerSession(
                _playerAuthToken,
                _playerAuthTokenType,
                session.Username,
                session.ExternalId,
                session.Roles,
                ApiBaseUrl);
            await PersistSettingsSnapshotAsync();
            return string.Empty;
        }
        catch (LauncherApiException apiException) when (
            apiException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var isBanNotice = TryShowBanNotice(apiException);
            if (!string.IsNullOrWhiteSpace(PlayerLoggedInAs))
            {
                RemoveStoredAccount(PlayerLoggedInAs);
            }

            ClearAuthenticatedPlayerSession();
            await PersistSettingsSnapshotAsync();
            return isBanNotice
                ? BanNoticeMessage
                : "Player session rejected by server (possibly banned). Login is required.";
        }
        catch (Exception ex)
        {
            return $"Failed to validate player session before launch: {ex.Message}";
        }
    }

    private async Task TryRefreshCurrentPlayerSessionStateAsync()
    {
        try
        {
            var session = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.GetSessionAsync(candidate, _playerAuthToken, _playerAuthTokenType),
                preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl());
            SetAuthenticatedPlayerSession(
                _playerAuthToken,
                _playerAuthTokenType,
                session.Username,
                session.ExternalId,
                session.Roles,
                ApiBaseUrl);
            await PersistSettingsSnapshotAsync();
        }
        catch (LauncherApiException apiException) when (
            apiException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var isBanNotice = TryShowBanNotice(apiException);
            if (!string.IsNullOrWhiteSpace(PlayerLoggedInAs))
            {
                RemoveStoredAccount(PlayerLoggedInAs);
            }

            ClearAuthenticatedPlayerSession();
            await PersistSettingsSnapshotAsync();
            StatusText = isBanNotice
                ? BanNoticeMessage
                : (_languageCode == "en"
                    ? "Player session expired. Login again."
                    : "Сессия игрока истекла. Войдите заново.");
        }
        catch (Exception ex)
        {
            _logService.LogError($"Player session refresh failed: {ex.Message}");
        }
    }

    private async Task<string> EnsureSelectedStoredAccountReadyForLaunchAsync()
    {
        if (!IsPlayerLoggedIn || SelectedStoredAccount is null || IsStoredAccountCurrentlyActive(SelectedStoredAccount))
        {
            return string.Empty;
        }

        return await ActivateStoredAccountAsync(SelectedStoredAccount, refreshLauncherContent: true);
    }

    private async Task<string> ActivateStoredAccountAsync(StoredPlayerAccount account, bool refreshLauncherContent)
    {
        var selectedUsername = (account.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selectedUsername))
        {
            return _languageCode == "en"
                ? "Selected account is invalid."
                : "Выбранный аккаунт некорректен.";
        }

        var token = (account.AuthToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            RemoveStoredAccount(selectedUsername);
            await PersistSettingsSnapshotAsync();
            return _languageCode == "en"
                ? "Saved account session is missing. Login again."
                : "У сохранённого аккаунта отсутствует сессия. Войдите заново.";
        }

        var accountApiBaseUrl = NormalizeBaseUrlOrEmpty(account.ApiBaseUrl);

        try
        {
            var session = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.GetSessionAsync(
                    candidate,
                    token,
                    string.IsNullOrWhiteSpace(account.AuthTokenType) ? "Bearer" : account.AuthTokenType),
                preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl(accountApiBaseUrl));

            var sessionUsername = (session.Username ?? string.Empty).Trim();
            var sessionExternalId = (session.ExternalId ?? string.Empty).Trim();
            var selectedExternalId = (account.ExternalId ?? string.Empty).Trim();
            var usernameMatches = string.Equals(
                selectedUsername,
                sessionUsername,
                StringComparison.OrdinalIgnoreCase);
            var externalIdMatches = string.IsNullOrWhiteSpace(selectedExternalId) ||
                                    string.IsNullOrWhiteSpace(sessionExternalId) ||
                                    string.Equals(
                                        selectedExternalId,
                                        sessionExternalId,
                                        StringComparison.OrdinalIgnoreCase);
            if (!usernameMatches || !externalIdMatches)
            {
                RemoveStoredAccount(selectedUsername);
                await PersistSettingsSnapshotAsync();
                _logService.LogInfo(
                    $"Stored account mismatch on activation. Selected={selectedUsername}({selectedExternalId}), " +
                    $"session={sessionUsername}({sessionExternalId}).");
                return _languageCode == "en"
                    ? "Saved account token belongs to another user. Login again."
                    : "Токен сохранённого аккаунта принадлежит другому пользователю. Войдите снова.";
            }

            SetAuthenticatedPlayerSession(
                token,
                account.AuthTokenType,
                sessionUsername,
                sessionExternalId,
                session.Roles,
                ApiBaseUrl);
            await RefreshPlayerCosmeticsAsync(sessionUsername);
            await PersistSettingsSnapshotAsync();
            if (refreshLauncherContent)
            {
                await RefreshCoreAsync();
            }

            return string.Empty;
        }
        catch (LauncherApiException apiException) when (
            apiException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var isBanNotice = TryShowBanNotice(apiException);
            RemoveStoredAccount(selectedUsername);
            await PersistSettingsSnapshotAsync();
            return isBanNotice
                ? BanNoticeMessage
                : (_languageCode == "en"
                    ? "Saved account session expired. Login again."
                    : "Сессия сохранённого аккаунта истекла. Войдите заново.");
        }
    }

    private bool IsStoredAccountCurrentlyActive(StoredPlayerAccount account)
    {
        var selectedUsername = (account.Username ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(selectedUsername) &&
            string.Equals(selectedUsername, PlayerLoggedInAs, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var selectedExternalId = (account.ExternalId ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(selectedExternalId) &&
               !string.IsNullOrWhiteSpace(_playerAuthExternalId) &&
               string.Equals(selectedExternalId, _playerAuthExternalId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PublicGameSessionStartResponse?> TryStartGameSessionAsync(ManagedServerItem server)
    {
        if (string.IsNullOrWhiteSpace(_playerAuthToken))
        {
            throw new InvalidOperationException("Player auth token is missing.");
        }

        try
        {
            var session = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.StartGameSessionAsync(
                    candidate,
                    _playerAuthToken,
                    _playerAuthTokenType,
                    new PublicGameSessionStartRequest
                    {
                        ServerId = server.ServerId,
                        ServerName = server.DisplayName
                    }),
                preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl());

            if (session.Limit > 0)
            {
                _logService.LogInfo(
                    $"Game session started: {session.SessionId} for {server.DisplayName}. " +
                    $"Active accounts on device: {session.ActiveAccountsOnDevice}/{session.Limit}.");
            }

            return session;
        }
        catch (LauncherApiException apiException) when (
            apiException.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden)
        {
            if (TryShowBanNotice(apiException))
            {
                StatusText = BanNoticeMessage;
                return null;
            }

            throw;
        }
    }

    private async Task RunGameSessionHeartbeatLoopAsync(
        PublicGameSessionStartResponse gameSession,
        CancellationToken cancellationToken)
    {
        var heartbeatIntervalSeconds = Math.Clamp(gameSession.HeartbeatIntervalSeconds, 10, 120);
        var delay = TimeSpan.FromSeconds(heartbeatIntervalSeconds);

        while (true)
        {
            await Task.Delay(delay, cancellationToken);

            try
            {
                await ExecuteAgainstApiFailoverAsync(
                    async candidate =>
                    {
                        await _launcherApiService.HeartbeatGameSessionAsync(
                            candidate,
                            _playerAuthToken,
                            _playerAuthTokenType,
                            gameSession.SessionId,
                            cancellationToken);
                        return true;
                    },
                    preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl(),
                    persistSuccess: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (LauncherApiException apiException) when (
                apiException.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logService.LogInfo(
                    $"Game session heartbeat stopped permanently for {gameSession.SessionId}: " +
                    $"{(int)apiException.StatusCode} {apiException.Message}");
                return;
            }
            catch (Exception ex)
            {
                _logService.LogInfo($"Game session heartbeat warning for {gameSession.SessionId}: {ex.Message}");
            }
        }
    }

    private async Task TryStopGameSessionAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(_playerAuthToken))
        {
            return;
        }

        try
        {
            await ExecuteAgainstApiFailoverAsync(
                async candidate =>
                {
                    await _launcherApiService.StopGameSessionAsync(
                        candidate,
                        _playerAuthToken,
                        _playerAuthTokenType,
                        sessionId);
                    return true;
                },
                preferredApiBaseUrl: ResolvePreferredPlayerApiBaseUrl(),
                persistSuccess: false);
        }
        catch (Exception ex)
        {
            _logService.LogInfo($"Game session stop warning for {sessionId}: {ex.Message}");
        }
    }

    private void SetAuthenticatedPlayerSession(
        string token,
        string tokenType,
        string username,
        string externalId,
        IEnumerable<string> roles,
        string sourceApiBaseUrl)
    {
        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("Authenticated username is empty.");
        }

        _playerAuthToken = token.Trim();
        _playerAuthTokenType = string.IsNullOrWhiteSpace(tokenType) ? "Bearer" : tokenType.Trim();
        _playerAuthExternalId = string.IsNullOrWhiteSpace(externalId)
            ? normalizedUsername
            : externalId.Trim();
        _playerAuthRoles = NormalizePlayerRoles(roles);
        _playerAuthApiBaseUrl = NormalizeBaseUrl(sourceApiBaseUrl);
        _allowAutoSessionRestore = true;
        UpsertStoredAccount(new StoredPlayerAccount
        {
            Username = normalizedUsername,
            AuthToken = _playerAuthToken,
            AuthTokenType = _playerAuthTokenType,
            ExternalId = _playerAuthExternalId,
            Roles = [.. _playerAuthRoles],
            ApiBaseUrl = _playerAuthApiBaseUrl,
            LastUsedAtUtc = DateTime.UtcNow
        });
        SetSelectedStoredAccount(ResolveStoredAccount(normalizedUsername));

        IsPlayerLoggedIn = true;
        IsSettingsOpen = false;
        ClearBanNotice();
        ResetTwoFactorState();
        PlayerPassword = string.Empty;
        PlayerLoggedInAs = normalizedUsername;
        PlayerUsername = normalizedUsername;
        AuthStatusText = F("status.loggedInAs", normalizedUsername, string.Join(", ", _playerAuthRoles));
        NotifyAccountPresentationChanged();
    }

    private async Task RefreshPlayerCosmeticsAsync(string username)
    {
        var normalized = username.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            HasSkin = false;
            HasCape = false;
            return;
        }

        try
        {
            HasSkin = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.HasSkinAsync(candidate, normalized),
                persistSuccess: false);
            HasCape = await ExecuteAgainstApiFailoverAsync(
                candidate => _launcherApiService.HasCapeAsync(candidate, normalized),
                persistSuccess: false);
        }
        catch (Exception ex)
        {
            HasSkin = false;
            HasCape = false;
            _logService.LogError($"Failed to refresh player cosmetics: {ex.Message}");
        }
    }

    private void ClearAuthenticatedPlayerSession()
    {
        _playerAuthToken = string.Empty;
        _playerAuthTokenType = "Bearer";
        _playerAuthExternalId = string.Empty;
        _playerAuthRoles = [];
        _playerAuthApiBaseUrl = string.Empty;
        PlayerLoggedInAs = string.Empty;
        NotifyAccountPresentationChanged();
    }

    private static List<string> NormalizePlayerRoles(IEnumerable<string>? roles)
    {
        var normalized = (roles ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add("player");
        }

        return normalized;
    }

    private static bool HasAdministrativeRole(IEnumerable<string> roles)
    {
        return roles.Any(role =>
            string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "administrator", StringComparison.OrdinalIgnoreCase));
    }

    private static List<StoredPlayerAccount> NormalizeStoredAccounts(IEnumerable<StoredPlayerAccount>? accounts)
    {
        var result = new List<StoredPlayerAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var account in accounts ?? [])
        {
            var username = (account.Username ?? string.Empty).Trim();
            var token = (account.AuthToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (!seen.Add(username))
            {
                continue;
            }

            result.Add(new StoredPlayerAccount
            {
                Username = username,
                AuthToken = token,
                AuthTokenType = string.IsNullOrWhiteSpace(account.AuthTokenType) ? "Bearer" : account.AuthTokenType.Trim(),
                ExternalId = string.IsNullOrWhiteSpace(account.ExternalId) ? username : account.ExternalId.Trim(),
                Roles = NormalizePlayerRoles(account.Roles),
                ApiBaseUrl = NormalizeBaseUrlOrEmpty(account.ApiBaseUrl),
                LastUsedAtUtc = account.LastUsedAtUtc == default ? DateTime.UtcNow : account.LastUsedAtUtc
            });
        }

        return result
            .OrderByDescending(x => x.LastUsedAtUtc)
            .ThenBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SetStoredAccounts(IEnumerable<StoredPlayerAccount> accounts)
    {
        var normalized = NormalizeStoredAccounts(accounts);
        StoredPlayerAccounts.Clear();
        foreach (var account in normalized)
        {
            StoredPlayerAccounts.Add(account);
        }
    }

    private StoredPlayerAccount? ResolveStoredAccount(string? username)
    {
        var key = (username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return StoredPlayerAccounts.FirstOrDefault(x =>
            string.Equals(x.Username, key, StringComparison.OrdinalIgnoreCase));
    }

    private void UpsertStoredAccount(StoredPlayerAccount account)
    {
        var normalized = NormalizeStoredAccounts([account]);
        if (normalized.Count == 0)
        {
            return;
        }

        var candidate = normalized[0];
        var existing = ResolveStoredAccount(candidate.Username);
        if (existing is not null)
        {
            existing.AuthToken = candidate.AuthToken;
            existing.AuthTokenType = candidate.AuthTokenType;
            existing.ExternalId = candidate.ExternalId;
            existing.Roles = candidate.Roles;
            existing.ApiBaseUrl = candidate.ApiBaseUrl;
            existing.LastUsedAtUtc = candidate.LastUsedAtUtc;
        }
        else
        {
            StoredPlayerAccounts.Add(candidate);
        }

        var ordered = StoredPlayerAccounts
            .OrderByDescending(x => x.LastUsedAtUtc)
            .ThenBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        StoredPlayerAccounts.Clear();
        foreach (var item in ordered)
        {
            StoredPlayerAccounts.Add(item);
        }
    }

    private void RemoveStoredAccount(string username)
    {
        var existing = ResolveStoredAccount(username);
        if (existing is null)
        {
            return;
        }

        StoredPlayerAccounts.Remove(existing);
        if (SelectedStoredAccount is not null &&
            string.Equals(SelectedStoredAccount.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            SetSelectedStoredAccount(StoredPlayerAccounts.FirstOrDefault());
        }
    }

    private void SetSelectedStoredAccount(StoredPlayerAccount? account)
    {
        SelectedStoredAccount = account;
    }

    private void ResetTwoFactorState()
    {
        IsTwoFactorStepActive = false;
        TwoFactorSetupSecret = string.Empty;
        TwoFactorSetupUri = string.Empty;
        PlayerTwoFactorCode = string.Empty;
        _pendingTwoFactorUsername = string.Empty;
        _pendingTwoFactorPassword = string.Empty;
    }

    private static string NormalizeTwoFactorCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return string.Empty;
        }

        var digits = new string(rawCode.Where(char.IsDigit).Take(6).ToArray());
        return digits;
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
            EnterBusyOperation();
            await action();
        }
        catch (Exception ex)
        {
            if (!TryShowBanNotice(ex))
            {
                StatusText = BuildStatusErrorText(ex);
            }
            _logService.LogError(ex.ToString());
        }
        finally
        {
            ExitBusyOperation();
        }
    }

    private void EnterBusyOperation()
    {
        _busyOperationCount++;
        if (!IsBusy)
        {
            IsBusy = true;
        }
    }

    private void ExitBusyOperation()
    {
        if (_busyOperationCount > 0)
        {
            _busyOperationCount--;
        }

        if (_busyOperationCount == 0 && IsBusy)
        {
            IsBusy = false;
        }
    }

    partial void OnIsPlayerLoggedInChanged(bool value)
    {
        VerifyFilesCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        ToggleSettingsCommand.NotifyCanExecuteChanged();
        SwitchAccountCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
        AddAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsLoginRequired));
        OnPropertyChanged(nameof(IsLauncherReady));
        OnPropertyChanged(nameof(ServerMonitoringText));
        NotifyAccountPresentationChanged();
        NotifyLauncherHeaderPresentationChanged();

        if (!value)
        {
            _serverOnlineRefreshTimer.Stop();
            _lastServerOnlineRefreshUtc = default;
            ClearAuthenticatedPlayerSession();
            IsSettingsOpen = false;
            ResetTwoFactorState();
            HasSkin = false;
            HasCape = false;
            AuthStatusText = T("status.notLoggedIn");
            foreach (var server in ManagedServers)
            {
                server.IsOnline = false;
                server.OnlinePlayers = 0;
                server.OnlineMaxPlayers = -1;
                server.OnlineStatusText = _languageCode == "en" ? "Monitoring disabled" : "Мониторинг выключен";
                server.OnlineStatusBrush = new SolidColorBrush(Color.Parse("#8799B5"));
            }

            return;
        }

        _lastServerOnlineRefreshUtc = default;
        OnPropertyChanged(nameof(ServerMonitoringText));
        if (!_serverOnlineRefreshTimer.IsEnabled)
        {
            _serverOnlineRefreshTimer.Start();
        }

        _ = RefreshServerOnlineStatusesAsync();
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

        await DownloadUpdatePackageCoreAsync(automatic: false);
    }

    private async Task<bool> DownloadUpdatePackageCoreAsync(bool automatic)
    {
        if (IsUpdateDownloading || !IsUpdateAvailable || string.IsNullOrWhiteSpace(UpdateDownloadUrl))
        {
            return false;
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
            if (!automatic)
            {
                StatusText = "Update package downloaded.";
            }

            return true;
        }
        catch (Exception ex)
        {
            UpdateDownloadStatusText = $"Download failed: {ex.Message}";
            StatusText = BuildStatusErrorText(ex);
            _logService.LogError(ex.ToString());
            return false;
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

        await ScheduleUpdateInstallAndShutdownAsync(automatic: false);
    }

    private async Task ScheduleUpdateInstallAndShutdownAsync(bool automatic)
    {
        try
        {
            if (!automatic)
            {
                await SaveSettingsAsync();
            }
            else
            {
                await PersistSettingsSnapshotAsync();
            }

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

    private async Task TryApplyAutomaticUpdateAsync()
    {
        if (_isAutomaticUpdateInProgress || !IsUpdateAvailable || string.IsNullOrWhiteSpace(UpdateDownloadUrl))
        {
            return;
        }

        _isAutomaticUpdateInProgress = true;
        try
        {
            var readyPackage = IsUpdatePackageReady &&
                               string.Equals(DownloadedUpdateVersion, LatestLauncherVersion, StringComparison.OrdinalIgnoreCase);

            if (!readyPackage)
            {
                readyPackage = await DownloadUpdatePackageCoreAsync(automatic: true);
            }

            if (!readyPackage)
            {
                return;
            }

            await ScheduleUpdateInstallAndShutdownAsync(automatic: true);
        }
        finally
        {
            _isAutomaticUpdateInProgress = false;
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

    private async Task ApplyLauncherDirectoryNameAsync(BrandingConfig? branding)
    {
        var previousDefaultInstallDirectory = _settingsService.GetDefaultInstallDirectory();
        _settingsService.ConfigureProjectDirectoryName(branding?.LauncherDirectoryName);
        var nextDefaultInstallDirectory = _settingsService.GetDefaultInstallDirectory();

        var shouldUseDefaultInstallDirectory = string.IsNullOrWhiteSpace(_settings.InstallDirectory) ||
                                               ArePathsEqual(_settings.InstallDirectory, previousDefaultInstallDirectory);
        if (!shouldUseDefaultInstallDirectory ||
            ArePathsEqual(_settings.InstallDirectory, nextDefaultInstallDirectory))
        {
            return;
        }

        _settings.InstallDirectory = nextDefaultInstallDirectory;
        InstallDirectory = nextDefaultInstallDirectory;
        await _settingsService.SaveAsync(_settings);
    }

    private void ApplyBranding(string apiBaseUrl, BrandingConfig? branding, int assetRefreshVersion)
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
        var deep = ParseColorOrFallback("#0B111B", "#0B111B");
        var surface = ParseColorOrFallback(branding.SurfaceColor, "#1A2944CC");
        var surfaceBorder = ParseColorOrFallback(branding.SurfaceBorderColor, "#3F6BA4");
        var textPrimary = ParseColorOrFallback(branding.TextPrimaryColor, "#EEF5FF");
        var textSecondary = ParseColorOrFallback(branding.TextSecondaryColor, "#A7BEDC");
        var primaryButton = ParseColorOrFallback(branding.PrimaryButtonColor, "#2C76F0");
        var primaryButtonBorder = ParseColorOrFallback(branding.PrimaryButtonBorderColor, "#5FA0FF");
        var primaryButtonText = ParseColorOrFallback(branding.PrimaryButtonTextColor, "#F7FBFF");
        var playButton = ParseColorOrFallback(branding.PlayButtonColor, "#10A879");
        var playButtonBorder = ParseColorOrFallback(branding.PlayButtonBorderColor, "#67D9B1");
        var playButtonText = ParseColorOrFallback(branding.PlayButtonTextColor, "#F7FBFF");
        var inputBackground = ParseColorOrFallback(branding.InputBackgroundColor, "#0B182BD9");
        var inputBorder = ParseColorOrFallback(branding.InputBorderColor, "#436A9F");
        var inputText = ParseColorOrFallback(branding.InputTextColor, "#EFF6FF");
        var listBackground = ParseColorOrFallback(branding.ListBackgroundColor, "#0D1B2FD9");
        var listBorder = ParseColorOrFallback(branding.ListBorderColor, "#3E669A");

        LauncherBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(BlendColors(primary, deep, 0.82), 0),
                new GradientStop(BlendColors(primary, deep, 0.9), 0.55),
                new GradientStop(BlendColors(accent, deep, 0.9), 1)
            }
        };

        HeroBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(BlendColors(primary, accent, 0.24), 0),
                new GradientStop(BlendColors(primary, deep, 0.38), 1)
            }
        };

        HeroBorderBrush = new SolidColorBrush(BlendColors(primary, accent, 0.35));
        LoginCardBackgroundBrush = new SolidColorBrush(surface);
        LoginCardBorderBrush = new SolidColorBrush(surfaceBorder);
        PanelBackgroundBrush = new SolidColorBrush(surface);
        PanelBorderBrush = new SolidColorBrush(surfaceBorder);
        PrimaryTextBrush = new SolidColorBrush(textPrimary);
        SecondaryTextBrush = new SolidColorBrush(textSecondary);
        PrimaryButtonBackgroundBrush = new SolidColorBrush(primaryButton);
        PrimaryButtonBorderBrush = new SolidColorBrush(primaryButtonBorder);
        PrimaryButtonForegroundBrush = new SolidColorBrush(primaryButtonText);
        PlayButtonBackgroundBrush = new SolidColorBrush(playButton);
        PlayButtonBorderBrush = new SolidColorBrush(playButtonBorder);
        PlayButtonForegroundBrush = new SolidColorBrush(playButtonText);
        InputBackgroundBrush = new SolidColorBrush(inputBackground);
        InputBorderBrush = new SolidColorBrush(inputBorder);
        InputForegroundBrush = new SolidColorBrush(inputText);
        ListBackgroundBrush = new SolidColorBrush(listBackground);
        ListBorderBrush = new SolidColorBrush(listBorder);
        NotifyApiRegionSelectionPresentationChanged();

        BrandingBackgroundOverlayOpacity = Math.Clamp(branding.BackgroundOverlayOpacity, 0, 0.95);
        LoginCardHorizontalAlignment = ParseLoginCardAlignment(branding.LoginCardPosition);

        var requestedWidth = branding.LoginCardWidth <= 0 ? 460 : branding.LoginCardWidth;
        LoginCardWidth = Math.Clamp(requestedWidth, 340, 640);

        var backgroundUrl = branding.BackgroundImageUrl;
        var launcherIconUrl = branding.LauncherIconUrl;
        var cachedBackground = TryResolveBrandingImageFromCache(apiBaseUrl, backgroundUrl);
        BrandingBackgroundImage = cachedBackground;
        ApplyLauncherWindowIconFromCache(apiBaseUrl, launcherIconUrl);
        QueueBrandingAssetRefresh(apiBaseUrl, assetRefreshVersion, backgroundUrl, launcherIconUrl);
    }

    private static bool ArePathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            var normalizedLeft = Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRight = Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
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

    private static string NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ResolveDefaultApiBaseUrl();
        }

        return value.Trim().TrimEnd('/');
    }

    private static string ResolveDefaultApiBaseUrl()
    {
        return NormalizeBaseUrlOrEmpty(TryResolveConfiguredApiBaseUrl());
    }

    private static string? TryResolveConfiguredApiBaseUrl()
    {
        var environmentBaseUrl = NormalizeBaseUrlOrEmpty(Environment.GetEnvironmentVariable(LauncherApiBaseUrlEnvVar));
        if (!string.IsNullOrWhiteSpace(environmentBaseUrl))
        {
            return environmentBaseUrl;
        }

        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;
        var bundledBaseUrl = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                LauncherApiBaseUrlAssemblyMetadataKey,
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        var normalizedBundledBaseUrl = NormalizeBaseUrlOrEmpty(bundledBaseUrl);
        if (!string.IsNullOrWhiteSpace(normalizedBundledBaseUrl))
        {
            return normalizedBundledBaseUrl;
        }

        return null;
    }

    private static string NormalizeBaseUrlOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().TrimEnd('/');
    }

    private static string NormalizePersistedApiBaseUrl(string? value, string? configuredApiBaseUrl)
    {
        var normalizedValue = NormalizeBaseUrlOrEmpty(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return string.Empty;
        }

        var normalizedConfiguredApiBaseUrl = NormalizeBaseUrlOrEmpty(configuredApiBaseUrl);
        if (IsImplicitLocalFallbackApiBaseUrl(normalizedValue, normalizedConfiguredApiBaseUrl))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(normalizedConfiguredApiBaseUrl) &&
            string.Equals(normalizedValue, normalizedConfiguredApiBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalizedValue;
    }

    private static bool IsImplicitLocalFallbackApiBaseUrl(string? value, string? configuredApiBaseUrl = null)
    {
        var normalizedValue = NormalizeBaseUrlOrEmpty(value);
        if (!string.Equals(normalizedValue, LocalFallbackApiBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedConfiguredApiBaseUrl = NormalizeBaseUrlOrEmpty(configuredApiBaseUrl);
        return string.IsNullOrWhiteSpace(normalizedConfiguredApiBaseUrl) ||
               !string.Equals(normalizedConfiguredApiBaseUrl, LocalFallbackApiBaseUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrimConfiguredApiBaseUrlReferences(LauncherSettings settings, string? configuredApiBaseUrl)
    {
        var changed = false;

        var normalizedSettingsApiBaseUrl = NormalizePersistedApiBaseUrl(settings.ApiBaseUrl, configuredApiBaseUrl);
        var expectedSettingsApiBaseUrl = string.IsNullOrWhiteSpace(NormalizeBaseUrlOrEmpty(configuredApiBaseUrl))
            ? normalizedSettingsApiBaseUrl
            : string.Empty;
        if (!string.Equals(
                expectedSettingsApiBaseUrl,
                NormalizeBaseUrlOrEmpty(settings.ApiBaseUrl),
                StringComparison.Ordinal))
        {
            settings.ApiBaseUrl = expectedSettingsApiBaseUrl;
            changed = true;
        }

        var normalizedPlayerAuthApiBaseUrl = NormalizePersistedApiBaseUrl(settings.PlayerAuthApiBaseUrl, configuredApiBaseUrl);
        if (!string.Equals(
                normalizedPlayerAuthApiBaseUrl,
                NormalizeBaseUrlOrEmpty(settings.PlayerAuthApiBaseUrl),
                StringComparison.Ordinal))
        {
            settings.PlayerAuthApiBaseUrl = normalizedPlayerAuthApiBaseUrl;
            changed = true;
        }

        foreach (var account in settings.PlayerAccounts ?? [])
        {
            var normalizedAccountApiBaseUrl = NormalizePersistedApiBaseUrl(account.ApiBaseUrl, configuredApiBaseUrl);
            if (string.Equals(
                    normalizedAccountApiBaseUrl,
                    NormalizeBaseUrlOrEmpty(account.ApiBaseUrl),
                    StringComparison.Ordinal))
            {
                continue;
            }

            account.ApiBaseUrl = normalizedAccountApiBaseUrl;
            changed = true;
        }

        var normalizedKnownApiBaseUrls = (settings.KnownApiBaseUrls ?? [])
            .Select(candidate => NormalizePersistedApiBaseUrl(candidate, configuredApiBaseUrl))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentKnownApiBaseUrls = (settings.KnownApiBaseUrls ?? [])
            .Select(NormalizeBaseUrlOrEmpty)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!normalizedKnownApiBaseUrls.SequenceEqual(currentKnownApiBaseUrls, StringComparer.OrdinalIgnoreCase))
        {
            settings.KnownApiBaseUrls = normalizedKnownApiBaseUrls;
            changed = true;
        }

        return changed;
    }

    private void MergeKnownApiBaseUrls(IEnumerable<string>? candidates)
    {
        foreach (var candidate in candidates ?? [])
        {
            var normalized = NormalizeBaseUrlOrEmpty(candidate);
            if (string.IsNullOrWhiteSpace(normalized) ||
                _knownApiBaseUrls.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _knownApiBaseUrls.Add(normalized);
        }
    }

    private IEnumerable<string> GetRegionalApiBaseUrlCandidates()
    {
        var candidates = new List<string>();

        void AddRegion(string regionCode)
        {
            var configuredApiBaseUrl = ResolveConfiguredApiBaseUrlForRegion(regionCode);
            if (string.IsNullOrWhiteSpace(configuredApiBaseUrl) ||
                candidates.Contains(configuredApiBaseUrl, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            candidates.Add(configuredApiBaseUrl);
        }

        var preferredRegionCode = NormalizeApiRegionCode(PreferredApiRegion);
        if (!string.IsNullOrWhiteSpace(preferredRegionCode))
        {
            AddRegion(preferredRegionCode);
            return candidates;
        }

        AddRegion("ru");
        AddRegion("eu");
        return candidates;
    }

    private IEnumerable<string> GetApiBaseUrlCandidates(string? preferredApiBaseUrl = null)
    {
        var candidates = new List<string>();

        void Add(string? value)
        {
            var normalized = NormalizeBaseUrlOrEmpty(value);
            if (string.IsNullOrWhiteSpace(normalized) ||
                !IsApiBaseUrlCandidateAllowedForSelectedRegion(normalized) ||
                candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            candidates.Add(normalized);
        }

        Add(preferredApiBaseUrl);
        foreach (var regionalApiBaseUrl in GetRegionalApiBaseUrlCandidates())
        {
            Add(regionalApiBaseUrl);
        }
        Add(ApiBaseUrl);
        Add(_playerAuthApiBaseUrl);
        Add(TryResolveConfiguredApiBaseUrl());

        foreach (var knownApiBaseUrl in _knownApiBaseUrls)
        {
            Add(knownApiBaseUrl);
        }

        foreach (var bundledFallback in ResolveBundledFallbackApiBaseUrls())
        {
            Add(bundledFallback);
        }

        return candidates;
    }

    private bool IsApiBaseUrlCandidateAllowedForSelectedRegion(string candidate)
    {
        var normalizedCandidate = NormalizeBaseUrlOrEmpty(candidate);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return false;
        }

        var preferredRegionCode = NormalizeApiRegionCode(PreferredApiRegion);
        if (string.IsNullOrWhiteSpace(preferredRegionCode))
        {
            return true;
        }

        var preferredApiBaseUrl = NormalizeBaseUrlOrEmpty(ResolveConfiguredApiBaseUrlForRegion(preferredRegionCode));
        if (string.IsNullOrWhiteSpace(preferredApiBaseUrl))
        {
            return true;
        }

        if (string.Equals(normalizedCandidate, preferredApiBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var regionCode in new[] { "ru", "eu" })
        {
            if (string.Equals(regionCode, preferredRegionCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var blockedApiBaseUrl = NormalizeBaseUrlOrEmpty(ResolveConfiguredApiBaseUrlForRegion(regionCode));
            if (!string.IsNullOrWhiteSpace(blockedApiBaseUrl) &&
                string.Equals(normalizedCandidate, blockedApiBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<T> ExecuteAgainstApiFailoverAsync<T>(
        Func<string, Task<T>> operation,
        string? preferredApiBaseUrl = null,
        bool persistSuccess = true)
    {
        Exception? lastError = null;
        foreach (var candidate in GetApiBaseUrlCandidates(preferredApiBaseUrl))
        {
            try
            {
                var result = await operation(candidate);
                MergeKnownApiBaseUrls([candidate]);
                if (persistSuccess)
                {
                    ApiBaseUrl = candidate;
                }

                return result;
            }
            catch (LauncherApiException apiException) when (ShouldTryNextApiBaseUrl(apiException))
            {
                lastError = apiException;
                _logService.LogInfo($"API candidate failed and will be skipped: {candidate} ({(int)apiException.StatusCode}) {apiException.Message}");
            }
            catch (HttpRequestException httpException)
            {
                lastError = httpException;
                _logService.LogInfo($"API candidate failed and will be skipped: {candidate} ({httpException.Message})");
            }
            catch (TaskCanceledException canceledException)
            {
                lastError = canceledException;
                _logService.LogInfo($"API candidate timed out and will be skipped: {candidate} ({canceledException.Message})");
            }
        }

        throw lastError ?? new InvalidOperationException("No reachable API endpoints are configured.");
    }

    private static bool ShouldTryNextApiBaseUrl(LauncherApiException apiException)
    {
        return apiException.StatusCode is
            HttpStatusCode.NotFound or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    private static IReadOnlyList<string> ResolveBundledFallbackApiBaseUrls()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;
        var rawValue = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                LauncherFallbackApiBaseUrlsAssemblyMetadataKey,
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeBaseUrlOrEmpty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyProfileRuntimeFallback(LauncherManifest manifest, string profileSlug)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(profileSlug))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(manifest.JavaRuntimeArtifactKey))
        {
            return;
        }

        if (!_profileBundledRuntimeKeys.TryGetValue(profileSlug.Trim(), out var bundledRuntimeKey) ||
            string.IsNullOrWhiteSpace(bundledRuntimeKey))
        {
            return;
        }

        manifest.JavaRuntimeArtifactKey = bundledRuntimeKey.Trim();
        _logService.LogInfo(
            $"Manifest runtime fallback applied from bootstrap profile key for '{profileSlug}': {bundledRuntimeKey}.");
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

    private static string ComputeDeviceUserName()
    {
        if (string.IsNullOrWhiteSpace(Environment.UserName))
        {
            return string.Empty;
        }

        var normalized = Environment.UserName.Trim().ToLowerInvariant();
        return normalized.Length > 128 ? normalized[..128] : normalized;
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

    private void ClearBanNotice()
    {
        IsBanNoticeVisible = false;
        BanNoticeTitle = string.Empty;
        BanNoticeMessage = string.Empty;
    }

    private bool TryShowBanNotice(Exception ex)
    {
        if (ex is not LauncherApiException apiException ||
            !TryCreateBanNotice(apiException, out var title, out var message))
        {
            return false;
        }

        BanNoticeTitle = title;
        BanNoticeMessage = message;
        IsBanNoticeVisible = true;
        AuthStatusText = message;
        StatusText = message;
        return true;
    }

    private bool TryCreateBanNotice(LauncherApiException apiException, out string title, out string message)
    {
        title = string.Empty;
        message = string.Empty;

        var errorCode = (apiException.ErrorCode ?? string.Empty).Trim().ToLowerInvariant();
        var rawMessage = (apiException.Message ?? string.Empty).Trim();

        if (errorCode == "hardware_ban" ||
            rawMessage.StartsWith("Hardware banned", StringComparison.OrdinalIgnoreCase))
        {
            title = _languageCode == "en"
                ? "HWID access blocked"
                : "Вход с этого устройства заблокирован";
            message = BuildBanNoticeMessage(
                rawMessage,
                "Hardware banned:",
                _languageCode == "en"
                    ? "This device is blocked from signing in."
                    : "Это устройство заблокировано для входа.");
            return true;
        }

        if (errorCode == "device_user_ban" ||
            rawMessage.StartsWith("Device user banned", StringComparison.OrdinalIgnoreCase))
        {
            title = _languageCode == "en"
                ? "Device account blocked"
                : "Учётная запись устройства заблокирована";
            message = BuildBanNoticeMessage(
                rawMessage,
                "Device user banned:",
                _languageCode == "en"
                    ? "This OS user is blocked from signing in."
                    : "Для этой системной учётной записи вход заблокирован.");
            return true;
        }

        if (errorCode == "account_ban" ||
            rawMessage.StartsWith("Account is banned", StringComparison.OrdinalIgnoreCase))
        {
            title = _languageCode == "en"
                ? "Account blocked"
                : "Аккаунт заблокирован";
            message = BuildBanNoticeMessage(
                rawMessage,
                "Account is banned:",
                _languageCode == "en"
                    ? "This account cannot sign in."
                    : "Для этого аккаунта вход запрещён.");
            return true;
        }

        if (errorCode == "device_account_limit")
        {
            title = _languageCode == "en"
                ? "Device limit reached"
                : "Лимит аккаунтов на устройстве";
            message = !string.IsNullOrWhiteSpace(rawMessage)
                ? rawMessage
                : _languageCode == "en"
                    ? "Too many active game accounts are already running from this computer."
                    : "С этого компьютера уже запущено максимально допустимое число игровых аккаунтов.";
            return true;
        }

        return false;
    }

    private static string BuildBanNoticeMessage(string rawMessage, string prefix, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(rawMessage) &&
            rawMessage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var detail = rawMessage[prefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return detail;
            }
        }

        if (!string.IsNullOrWhiteSpace(rawMessage) &&
            !string.Equals(rawMessage, prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rawMessage, prefix.TrimEnd(':'), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rawMessage, "Account is banned.", StringComparison.OrdinalIgnoreCase))
        {
            return rawMessage;
        }

        return fallback;
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

        if (ex is UnauthorizedAccessException || ex.InnerException is UnauthorizedAccessException)
        {
            return _languageCode == "en"
                ? "File access denied. Close Minecraft/Java, check antivirus locks, and make sure the install folder is writable."
                : "Нет доступа к файлу или папке. Закрой Minecraft/Java, проверь блокировку антивирусом и права на папку установки.";
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

    private static string NormalizeNewsScopeType(string? rawScopeType)
    {
        var normalized = (rawScopeType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "profile" => "profile",
            "server" => "server",
            _ => "global"
        };
    }

    private static string NormalizeNewsScopeId(string? rawScopeId)
    {
        return string.IsNullOrWhiteSpace(rawScopeId) ? string.Empty : rawScopeId.Trim();
    }

    private string BuildNewsScopeLabel(string? rawScopeType, string? rawScopeName)
    {
        var scopeType = NormalizeNewsScopeType(rawScopeType);
        var scopeName = (rawScopeName ?? string.Empty).Trim();
        return scopeType switch
        {
            "profile" when !string.IsNullOrWhiteSpace(scopeName) => _languageCode == "en" ? $"Profile: {scopeName}" : $"Профиль: {scopeName}",
            "server" when !string.IsNullOrWhiteSpace(scopeName) => _languageCode == "en" ? $"Server: {scopeName}" : $"Сервер: {scopeName}",
            _ => _languageCode == "en" ? "All branches" : "Все ветки"
        };
    }

    private string BuildNewsMeta(string sourceRaw, bool pinned, DateTime createdAtUtc, string? scopeTypeRaw, string? scopeNameRaw)
    {
        var source = string.IsNullOrWhiteSpace(sourceRaw) ? "manual" : sourceRaw.Trim();
        if (source.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            source = T("news.source.manual");
        }

        var localTime = createdAtUtc.ToLocalTime().ToString("g");
        var scopeLabel = BuildNewsScopeLabel(scopeTypeRaw, scopeNameRaw);
        var baseMeta = pinned
            ? F("news.meta.pinned", source, localTime)
            : F("news.meta.regular", source, localTime);
        return $"{scopeLabel} | {baseMeta}";
    }

    private void RefreshVisibleNewsItems()
    {
        var selectedServerId = SelectedServer?.ServerId.ToString();
        var selectedProfileId = SelectedServer?.ProfileId.ToString();
        var filteredNews = _allNewsItems
            .Where(item =>
            {
                var scopeType = NormalizeNewsScopeType(item.ScopeType);
                var scopeId = NormalizeNewsScopeId(item.ScopeId);
                return scopeType == "global" ||
                       (scopeType == "profile" &&
                        !string.IsNullOrWhiteSpace(selectedProfileId) &&
                        string.Equals(scopeId, selectedProfileId, StringComparison.OrdinalIgnoreCase)) ||
                       (scopeType == "server" &&
                        !string.IsNullOrWhiteSpace(selectedServerId) &&
                        string.Equals(scopeId, selectedServerId, StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(item => item.Pinned)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ToList();

        NewsItems.Clear();
        foreach (var item in filteredNews)
        {
            NewsItems.Add(item);
        }

        SelectedNewsItem = NewsItems.FirstOrDefault();
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

    private void QueueBrandingAssetRefresh(string apiBaseUrl, int assetRefreshVersion, string? backgroundImageUrl, string? launcherIconUrl)
    {
        var backgroundTask = string.IsNullOrWhiteSpace(backgroundImageUrl)
            ? Task.FromResult<IImage?>(null)
            : ResolveBrandingImageAsync(apiBaseUrl, backgroundImageUrl);
        var windowIconTask = string.IsNullOrWhiteSpace(launcherIconUrl)
            ? Task.FromResult<Avalonia.Controls.WindowIcon?>(null)
            : ResolveLauncherWindowIconAsync(apiBaseUrl, launcherIconUrl);

        _ = Task.Run(async () =>
        {
            try
            {
                var backgroundImage = await backgroundTask;
                if (IsCurrentAssetRefresh(assetRefreshVersion))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsCurrentAssetRefresh(assetRefreshVersion))
                        {
                            BrandingBackgroundImage = backgroundImage;
                        }
                    });
                }

                var brandingIcon = await windowIconTask;
                if (IsCurrentAssetRefresh(assetRefreshVersion))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsCurrentAssetRefresh(assetRefreshVersion))
                        {
                            ApplyResolvedWindowIcon(brandingIcon);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Branding asset refresh failed: {ex.Message}");
            }
        });
    }

    private void QueueServerIconRefresh(
        string apiBaseUrl,
        int assetRefreshVersion,
        IReadOnlyCollection<(Guid ServerId, string ServerIconUrl, string ProfileIconUrl)> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var candidate in candidates)
            {
                if (!IsCurrentAssetRefresh(assetRefreshVersion))
                {
                    return;
                }

                try
                {
                    var icon = await ResolveServerIconAsync(apiBaseUrl, candidate.ServerIconUrl, candidate.ProfileIconUrl);
                    if (icon is null || !IsCurrentAssetRefresh(assetRefreshVersion))
                    {
                        continue;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!IsCurrentAssetRefresh(assetRefreshVersion))
                        {
                            return;
                        }

                        var target = ManagedServers.FirstOrDefault(server => server.ServerId == candidate.ServerId);
                        if (target is not null)
                        {
                            target.Icon = icon;
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Server icon refresh failed for {candidate.ServerId}: {ex.Message}");
                }
            }
        });
    }

    private bool IsCurrentAssetRefresh(int assetRefreshVersion) =>
        assetRefreshVersion == Volatile.Read(ref _assetRefreshVersion);

    private IImage? TryResolveServerIconFromCache(string apiBaseUrl, string? serverIconUrl, string? profileIconUrl)
    {
        var candidates = new[] { serverIconUrl, profileIconUrl };
        foreach (var candidate in candidates)
        {
            var absoluteUrl = ToAbsoluteUrl(apiBaseUrl, candidate);
            if (string.IsNullOrWhiteSpace(absoluteUrl))
            {
                continue;
            }

            var cached = TryResolveImageFromCache(_iconCache, absoluteUrl);
            if (cached is not null)
            {
                return cached;
            }
        }

        return null;
    }

    private IImage? TryResolveBrandingImageFromCache(string apiBaseUrl, string? brandingImageUrl)
    {
        var absoluteUrl = ToAbsoluteUrl(apiBaseUrl, brandingImageUrl);
        return string.IsNullOrWhiteSpace(absoluteUrl)
            ? null
            : TryResolveImageFromCache(_brandingImageCache, absoluteUrl);
    }

    private void ApplyLauncherWindowIconFromCache(string apiBaseUrl, string? launcherIconUrl)
    {
        ApplyResolvedWindowIcon(TryResolveLauncherWindowIconFromCache(apiBaseUrl, launcherIconUrl));
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

            var cached = TryResolveImageFromCache(_iconCache, absoluteUrl);
            if (cached is not null)
            {
                return cached;
            }

            var payload = await DownloadAssetPayloadAsync(absoluteUrl);
            if (payload is null)
            {
                lock (_assetCacheSyncRoot)
                {
                    _iconCache[absoluteUrl] = null;
                }
                continue;
            }

            var bitmap = CreateBitmap(payload);
            lock (_assetCacheSyncRoot)
            {
                _iconCache[absoluteUrl] = bitmap;
            }

            if (bitmap is not null)
            {
                return bitmap;
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

        var cached = TryResolveImageFromCache(_brandingImageCache, absoluteUrl);
        if (cached is not null)
        {
            return cached;
        }

        var payload = await DownloadAssetPayloadAsync(absoluteUrl);
        if (payload is null)
        {
            lock (_assetCacheSyncRoot)
            {
                _brandingImageCache[absoluteUrl] = null;
            }
            return null;
        }

        var bitmap = CreateBitmap(payload);
        lock (_assetCacheSyncRoot)
        {
            _brandingImageCache[absoluteUrl] = bitmap;
        }

        return bitmap;
    }

    private void ApplyResolvedWindowIcon(Avalonia.Controls.WindowIcon? brandingIcon)
    {
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow is null)
        {
            return;
        }

        _defaultWindowIcon ??= mainWindow.Icon;
        mainWindow.Icon = brandingIcon ?? _defaultWindowIcon;
    }

    private Avalonia.Controls.WindowIcon? TryResolveLauncherWindowIconFromCache(string apiBaseUrl, string? launcherIconUrl)
    {
        var absoluteUrl = ToAbsoluteUrl(apiBaseUrl, launcherIconUrl);
        if (string.IsNullOrWhiteSpace(absoluteUrl))
        {
            return null;
        }

        byte[]? cachedPayload;
        lock (_assetCacheSyncRoot)
        {
            if (_windowIconCache.TryGetValue(absoluteUrl, out cachedPayload))
            {
                return CreateWindowIcon(cachedPayload);
            }
        }

        cachedPayload = TryReadCachedAssetPayload(absoluteUrl);
        if (cachedPayload is null)
        {
            return null;
        }

        lock (_assetCacheSyncRoot)
        {
            _windowIconCache[absoluteUrl] = cachedPayload;
        }

        return CreateWindowIcon(cachedPayload);
    }

    private async Task<Avalonia.Controls.WindowIcon?> ResolveLauncherWindowIconAsync(string apiBaseUrl, string? launcherIconUrl)
    {
        var absoluteUrl = ToAbsoluteUrl(apiBaseUrl, launcherIconUrl);
        if (string.IsNullOrWhiteSpace(absoluteUrl))
        {
            return null;
        }

        var cached = TryResolveLauncherWindowIconFromCache(apiBaseUrl, launcherIconUrl);
        if (cached is not null)
        {
            return cached;
        }

        var payload = await DownloadAssetPayloadAsync(absoluteUrl);
        if (payload is null)
        {
            lock (_assetCacheSyncRoot)
            {
                _windowIconCache[absoluteUrl] = null;
            }
            return null;
        }

        lock (_assetCacheSyncRoot)
        {
            _windowIconCache[absoluteUrl] = payload;
        }

        return CreateWindowIcon(payload);
    }

    private static Avalonia.Controls.WindowIcon? CreateWindowIcon(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        var stream = new MemoryStream(payload, writable: false);
        return new Avalonia.Controls.WindowIcon(stream);
    }

    private IImage? TryResolveImageFromCache(Dictionary<string, IImage?> cache, string absoluteUrl)
    {
        lock (_assetCacheSyncRoot)
        {
            if (cache.TryGetValue(absoluteUrl, out var cached))
            {
                return cached;
            }
        }

        var payload = TryReadCachedAssetPayload(absoluteUrl);
        if (payload is null)
        {
            return null;
        }

        var image = CreateBitmap(payload);
        lock (_assetCacheSyncRoot)
        {
            cache[absoluteUrl] = image;
        }

        return image;
    }

    private Bitmap? CreateBitmap(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }

        try
        {
            using var memory = new MemoryStream(payload, writable: false);
            return new Bitmap(memory);
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]?> DownloadAssetPayloadAsync(string absoluteUrl)
    {
        try
        {
            var payload = await _iconHttpClient.GetByteArrayAsync(absoluteUrl);
            TryWriteCachedAssetPayload(absoluteUrl, payload);
            return payload;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? TryReadCachedAssetPayload(string absoluteUrl)
    {
        try
        {
            var cachePath = GetAssetCachePath(absoluteUrl);
            return File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private void TryWriteCachedAssetPayload(string absoluteUrl, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        try
        {
            var cachePath = GetAssetCachePath(absoluteUrl);
            var cacheDirectory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            File.WriteAllBytes(cachePath, payload);
        }
        catch
        {
        }
    }

    private string GetAssetCachePath(string absoluteUrl)
    {
        var uri = new Uri(absoluteUrl, UriKind.Absolute);
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10)
        {
            extension = ".bin";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(absoluteUrl))).ToLowerInvariant();
        var settingsDirectory = Path.GetDirectoryName(_settingsService.GetSettingsFilePath()) ?? AppContext.BaseDirectory;
        return Path.Combine(settingsDirectory, "asset-cache", $"{hash}{extension}");
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
        return "main";
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
        _isSyncingRouteOption = true;
        RouteOptions.Clear();
        RouteOptions.Add(new LocalizedOption { Value = "main", Label = T("route.main") });
        SelectedRouteOption = RouteOptions[0];
        _isSyncingRouteOption = false;
    }

    private static bool SupportsRuRoute(ManagedServerItem server)
    {
        return !string.IsNullOrWhiteSpace(server.RuProxyAddress);
    }

    private GameLaunchRoute ResolveLaunchRoute(ManagedServerItem server)
    {
        return new GameLaunchRoute
        {
            RouteCode = "main",
            Address = server.MainAddress.Trim(),
            Port = server.MainPort,
            PreferredJarPath = server.MainJarPath.Trim(),
            McVersion = server.McVersion
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
            ProfileId = server.ProfileId,
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
            Icon = server.Icon,
            IsOnline = server.IsOnline,
            OnlinePlayers = server.OnlinePlayers,
            OnlineMaxPlayers = server.OnlineMaxPlayers,
            OnlineStatusText = server.OnlineStatusText,
            OnlineStatusBrush = server.OnlineStatusBrush,
            OnlineLastCheckedAtUtc = server.OnlineLastCheckedAtUtc
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

        var localizedNewsItems = _allNewsItems.Select(item => new LauncherNewsItem
        {
            Id = item.Id,
            Title = item.Title,
            Body = item.Body,
            Preview = item.Preview,
            Source = item.Source,
            ScopeType = item.ScopeType,
            ScopeId = item.ScopeId,
            ScopeName = item.ScopeName,
            Pinned = item.Pinned,
            CreatedAtUtc = item.CreatedAtUtc,
            Meta = BuildNewsMeta(item.Source, item.Pinned, item.CreatedAtUtc, item.ScopeType, item.ScopeName)
        }).ToList();

        _allNewsItems.Clear();
        _allNewsItems.AddRange(localizedNewsItems);

        RefreshVisibleNewsItems();
        if (selectedNewsId.HasValue)
        {
            SelectedNewsItem = NewsItems.FirstOrDefault(x => x.Id == selectedNewsId.Value) ?? SelectedNewsItem;
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
        OnPropertyChanged(nameof(AddAccountButtonText));
        OnPropertyChanged(nameof(DeleteAccountButtonText));
        OnPropertyChanged(nameof(BanNoticeEyebrowText));
        OnPropertyChanged(nameof(BanNoticeDismissButtonText));
        OnPropertyChanged(nameof(LogoutButtonText));
        OnPropertyChanged(nameof(SwitchAccountButtonText));
        OnPropertyChanged(nameof(SavedAccountsLabelText));
        OnPropertyChanged(nameof(TwoFactorHintText));
        OnPropertyChanged(nameof(SkinStatusText));
        OnPropertyChanged(nameof(CapeStatusText));
        OnPropertyChanged(nameof(RuntimeHeaderText));
        NotifyApiRegionSelectionPresentationChanged();
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
        OnPropertyChanged(nameof(SelectedNewsTitle));
        OnPropertyChanged(nameof(SelectedNewsMeta));
        OnPropertyChanged(nameof(SelectedNewsBody));
        OnPropertyChanged(nameof(SelectedNewsHeadlineText));
        OnPropertyChanged(nameof(SelectedNewsBodyText));
        OnPropertyChanged(nameof(SelectedNewsMetaText));
        OnPropertyChanged(nameof(HasSelectedNews));
        OnPropertyChanged(nameof(HasSelectedNewsPlaceholder));
        OnPropertyChanged(nameof(EmptyNewsText));
        OnPropertyChanged(nameof(HasMultipleStoredAccounts));
        NotifyAccountPresentationChanged();
        OnPropertyChanged(nameof(ServerMonitoringText));
        NotifyLauncherHeaderPresentationChanged();
    }

    private void NotifyAccountPresentationChanged()
    {
        OnPropertyChanged(nameof(AccountPanelTitle));
        OnPropertyChanged(nameof(AccountPanelSubtitle));
        OnPropertyChanged(nameof(HasAccountPanelSubtitle));
        OnPropertyChanged(nameof(HasAdminRoleBanner));
        OnPropertyChanged(nameof(AdminRoleBannerText));
    }

    private void NotifyLauncherHeaderPresentationChanged()
    {
        OnPropertyChanged(nameof(LauncherHeaderStatusText));
        OnPropertyChanged(nameof(HasLauncherHeaderStatusText));
    }

    private readonly record struct ServerOnlineProbeResult(
        Guid ServerId,
        bool IsOnline,
        int OnlinePlayers,
        int OnlineMaxPlayers);

    private readonly record struct ServerEndpoint(string Host, int Port);

    private sealed class MinecraftStatusPayload
    {
        public MinecraftStatusPlayers? Players { get; set; }
    }

    private sealed class MinecraftStatusPlayers
    {
        public int Online { get; set; }
        public int Max { get; set; }
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

