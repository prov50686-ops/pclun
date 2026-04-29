using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PcLun.Services;
using ReactiveUI;

namespace PcLun.ViewModels;

public partial class MainViewModel : ReactiveObject
{
    private readonly Window? _owner;
    private readonly LauncherSettings _settings;
    private bool _suppressSave;

    public MainViewModel() : this(null) { }

    public MainViewModel(Window? owner)
    {
        _owner = owner;

        _settings = LauncherSettings.Load();

        // последний сохранённый аккаунт (если есть) переопределяет ник
        var saved = AuthService.LoadSaved();
        if (saved is not null)
        {
            _settings.Nickname = saved.Name;
            _settings.AuthMode = saved.Type == "msa" ? 1 : 0;
        }

        if (_settings.RamMb < 512) _settings.RamMb = SystemInfo.RecommendedRamMb();
        var total = SystemInfo.TotalRamMb;
        MaxRamMb = (int)Math.Max(2048L, total > 0 ? total - 1024 : 8192);

        // популярные суррогаты-серверы (адреса публичных тестовых)
        ServerSuggestions = new ObservableCollection<string>
        {
            "play.hypixel.net",
            "mc.hypixel.net",
            "play.cubecraft.net",
            "hub.mineplex.com",
            "play.purpleprison.net",
            "play.pika-network.net",
            "mc.complex-gaming.net",
            "play.craftrise.com.tr",
            "localhost:25565"
        };

        PlayCommand = ReactiveCommand.CreateFromTask(PlayAsync, this.WhenAnyValue(x => x.CanPlay));
        MicrosoftLoginCommand = ReactiveCommand.CreateFromTask(MicrosoftLoginAsync);
        LogoutCommand = ReactiveCommand.Create(Logout);
        PickJavaCommand = ReactiveCommand.CreateFromTask(PickJavaAsync);
        OpenGameDirCommand = ReactiveCommand.Create(() => OpenFolder(Paths.GameDir));
        OpenModsDirCommand = ReactiveCommand.Create(() => OpenFolder(Path.Combine(Paths.GameDir, "mods")));
        OpenScreenshotsDirCommand = ReactiveCommand.Create(() => OpenFolder(Path.Combine(Paths.GameDir, "screenshots")));
        ClearCacheCommand = ReactiveCommand.CreateFromTask(ClearCacheAsync);
        RefreshLogCommand = ReactiveCommand.Create(RefreshLog);
        ClearLogCommand = ReactiveCommand.Create(ClearLog);
        PickServerCommand = ReactiveCommand.Create<string>(PickServer);

        // ---- 0.3 features: content, backups, community ----
        CheckUpdateCommand = ReactiveCommand.CreateFromTask(CheckUpdateAsync);
        OpenLatestReleaseCommand = ReactiveCommand.Create(OpenLatestRelease);
        ScanCrashesCommand = ReactiveCommand.Create(ScanCrashes);
        OpenCrashCommand = ReactiveCommand.Create<CrashAnalyzer.CrashEntry?>(OpenCrash);
        RefreshContentCommand = ReactiveCommand.Create(RefreshContent);
        ToggleContentCommand = ReactiveCommand.Create<ContentItem?>(ToggleContent);
        DeleteContentCommand = ReactiveCommand.Create<ContentItem?>(DeleteContent);
        ImportModCommand = ReactiveCommand.CreateFromTask(() => ImportContentAsync(ContentKind.Mods));
        ImportShaderCommand = ReactiveCommand.CreateFromTask(() => ImportContentAsync(ContentKind.Shaderpacks));
        ImportResourcepackCommand = ReactiveCommand.CreateFromTask(() => ImportContentAsync(ContentKind.Resourcepacks));
        InstallPerformancePackCommand = ReactiveCommand.CreateFromTask(InstallPerformancePackAsync);
        DownloadShaderCommand = ReactiveCommand.CreateFromTask<ShaderpackInfo?>(DownloadShaderAsync);
        RefreshBackupsCommand = ReactiveCommand.Create(RefreshBackups);
        CreateBackupCommand = ReactiveCommand.CreateFromTask(CreateBackupAsync);
        RestoreBackupCommand = ReactiveCommand.CreateFromTask<BackupInfo?>(RestoreBackupAsync);
        DeleteBackupCommand = ReactiveCommand.Create<BackupInfo?>(DeleteBackup);
        OpenBackupsFolderCommand = ReactiveCommand.Create(() => OpenFolder(BackupService.BackupRoot));
        RefreshScreenshotsCommand = ReactiveCommand.Create(RefreshScreenshots);
        ExportSettingsCommand = ReactiveCommand.CreateFromTask(ExportSettingsAsync);
        ImportSettingsCommand = ReactiveCommand.CreateFromTask(ImportSettingsAsync);
        RefreshLeaderboardCommand = ReactiveCommand.CreateFromTask(RefreshLeaderboardAsync);
        RefreshNewsCommand = ReactiveCommand.CreateFromTask(RefreshNewsAsync);
        OpenScreenshotCommand = ReactiveCommand.Create<string?>(OpenScreenshot);

        UpdateRamHint();
        RefreshLog();
        RefreshContent();
        RefreshBackups();
        RefreshScreenshots();
        UpdatePlayerStats();
        UpdateSkinUrls();

        _ = StartOnlineLoopAsync();
        _ = InitExtrasAsync();

        InitProSurface();

        // Авто-сохранение при изменении любого свойства настроек.
        this.PropertyChanged += (_, e) =>
        {
            if (_suppressSave) return;
            // не сохраняем чисто UI-флаги типа IsHomeTab/StatusText/ProgressPercent.
            switch (e.PropertyName)
            {
                case null:
                case nameof(IsBusy):
                case nameof(IsHomeTab):
                case nameof(IsServersTab):
                case nameof(IsGraphicsTab):
                case nameof(IsOptifineTab):
                case nameof(IsJavaTab):
                case nameof(IsAccountTab):
                case nameof(IsToolsTab):
                case nameof(IsAboutTab):
                case nameof(IsContentTab):
                case nameof(IsCommunityTab):
                case nameof(StatusText):
                case nameof(ProgressPercent):
                case nameof(IsIndeterminate):
                case nameof(OnlineStatusText):
                case nameof(LogText):
                case nameof(AuthStatus):
                case nameof(CanPlay):
                case nameof(PlayButtonText):
                case nameof(NicknameInitial):
                case nameof(AccountModeLabel):
                case nameof(ProfileBadge):
                case nameof(RamBadge):
                case nameof(ResolutionBadge):
                case nameof(RamMbDisplay):
                case nameof(MaxFpsDisplay):
                case nameof(EntityDistanceDisplay):
                case nameof(FpsProfileDescription):
                case nameof(RamHint):
                case nameof(ResolvedJavaPath):
                    return;
            }
            _settings.Save();
        };
    }

    // ---------- tabs ----------
    // Один индекс активной вкладки — гарантирует mutual exclusion.
    private int _activeTab = 0;
    public int ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab == value) return;
            _activeTab = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsHomeTab));
            this.RaisePropertyChanged(nameof(IsServersTab));
            this.RaisePropertyChanged(nameof(IsGraphicsTab));
            this.RaisePropertyChanged(nameof(IsOptifineTab));
            this.RaisePropertyChanged(nameof(IsJavaTab));
            this.RaisePropertyChanged(nameof(IsAccountTab));
            this.RaisePropertyChanged(nameof(IsToolsTab));
            this.RaisePropertyChanged(nameof(IsAboutTab));
            this.RaisePropertyChanged(nameof(IsContentTab));
            this.RaisePropertyChanged(nameof(IsCommunityTab));
            this.RaisePropertyChanged(nameof(IsProTab));
        }
    }

    public bool IsHomeTab { get => ActiveTab == 0; set { if (value) ActiveTab = 0; } }
    public bool IsServersTab { get => ActiveTab == 1; set { if (value) ActiveTab = 1; } }
    public bool IsGraphicsTab { get => ActiveTab == 2; set { if (value) ActiveTab = 2; } }
    public bool IsOptifineTab { get => ActiveTab == 3; set { if (value) ActiveTab = 3; } }
    public bool IsJavaTab { get => ActiveTab == 4; set { if (value) ActiveTab = 4; } }
    public bool IsAccountTab { get => ActiveTab == 5; set { if (value) ActiveTab = 5; } }
    public bool IsToolsTab { get => ActiveTab == 6; set { if (value) ActiveTab = 6; } }
    public bool IsAboutTab { get => ActiveTab == 7; set { if (value) ActiveTab = 7; } }
    public bool IsContentTab { get => ActiveTab == 8; set { if (value) ActiveTab = 8; } }
    public bool IsCommunityTab { get => ActiveTab == 9; set { if (value) ActiveTab = 9; } }

    // ---------- player ----------
    public string Nickname
    {
        get => _settings.Nickname;
        set
        {
            if (_settings.Nickname == value) return;
            _settings.Nickname = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(NicknameInitial));
            this.RaisePropertyChanged(nameof(CanPlay));
            UpdateSkinUrls();
        }
    }

    public string NicknameInitial =>
        !string.IsNullOrWhiteSpace(_settings.Nickname) ? _settings.Nickname.Trim()[..1].ToUpper() : "?";

    public int AuthModeIndex
    {
        get => _settings.AuthMode;
        set { if (_settings.AuthMode == value) return; _settings.AuthMode = value; this.RaisePropertyChanged(); this.RaisePropertyChanged(nameof(AccountModeLabel)); }
    }

    public string AccountModeLabel => _settings.AuthMode == 1 ? "Microsoft" : "Офлайн";

    // ---------- ram ----------
    public int MaxRamMb { get; }

    public int RamMb
    {
        get => _settings.RamMb;
        set
        {
            if (_settings.RamMb == value) return;
            _settings.RamMb = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(RamMbDisplay));
            this.RaisePropertyChanged(nameof(RamBadge));
            UpdateRamHint();
        }
    }

    public string RamMbDisplay => $"{_settings.RamMb} МБ";

    private string _ramHint = "";
    public string RamHint { get => _ramHint; set => this.RaiseAndSetIfChanged(ref _ramHint, value); }

    private void UpdateRamHint()
    {
        var rec = SystemInfo.RecommendedRamMb();
        var total = SystemInfo.TotalRamMb;
        RamHint = total > 0
            ? $"Системная RAM: {total} МБ. Рекомендуем: {rec} МБ."
            : $"Рекомендуем для слабых ПК: 1024–2048 МБ.";
    }

    // ---------- FPS profile ----------
    public int FpsProfileIndex
    {
        get => _settings.FpsProfile;
        set
        {
            if (_settings.FpsProfile == value) return;
            try
            {
                _suppressSave = true;
                _settings.ApplyProfile(value);
            }
            finally { _suppressSave = false; }
            // raise everything
            RaiseAllSettingsChanged();
            _settings.Save();
        }
    }

    public string FpsProfileDescription => _settings.FpsProfile switch
    {
        0 => "🥔 Picture-перфекционизм наоборот: render 2, всё анимированное выкл., AA/AF off. Только цифры FPS.",
        1 => "Render 4, минимум частиц, без облаков/AO/анимаций. Цель — максимум FPS на слабом ПК.",
        2 => "Render 8, средние настройки, OptiFine оптимизации сохранены, но картинка приятнее.",
        3 => "Render 12, Fancy graphics, все анимации, для мощных ПК.",
        _ => ""
    };

    public string ProfileBadge => _settings.FpsProfile switch
    {
        0 => "🥔 Potato",
        1 => "⚡ Ultra FPS",
        2 => "◐ Balanced",
        3 => "✦ Quality",
        _ => ""
    };

    public string RamBadge => $"{_settings.RamMb} МБ ОЗУ";
    public string ResolutionBadge => _settings.Fullscreen ? "Полный экран" : $"{_settings.WindowWidth}×{_settings.WindowHeight}";

    // ---------- graphics passthroughs ----------
    public int RenderDistance { get => _settings.RenderDistance; set => SetSetting(value, _settings.RenderDistance, v => _settings.RenderDistance = v); }
    public int SimulationDistance { get => _settings.SimulationDistance; set => SetSetting(value, _settings.SimulationDistance, v => _settings.SimulationDistance = v); }
    public int MaxFps
    {
        get => _settings.MaxFps;
        set
        {
            if (_settings.MaxFps == value) return;
            _settings.MaxFps = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(MaxFpsDisplay));
        }
    }
    public string MaxFpsDisplay => _settings.MaxFps >= 260 ? "∞" : _settings.MaxFps.ToString();

    public int GraphicsMode { get => _settings.GraphicsMode; set => SetSetting(value, _settings.GraphicsMode, v => _settings.GraphicsMode = v); }
    public int Particles { get => _settings.Particles; set => SetSetting(value, _settings.Particles, v => _settings.Particles = v); }
    public int SmoothLighting { get => _settings.SmoothLighting; set => SetSetting(value, _settings.SmoothLighting, v => _settings.SmoothLighting = v); }
    public bool Clouds { get => _settings.Clouds; set => SetSetting(value, _settings.Clouds, v => _settings.Clouds = v); }
    public bool EntityShadows { get => _settings.EntityShadows; set => SetSetting(value, _settings.EntityShadows, v => _settings.EntityShadows = v); }
    public bool VSync { get => _settings.VSync; set => SetSetting(value, _settings.VSync, v => _settings.VSync = v); }
    public int MipmapLevels { get => _settings.MipmapLevels; set => SetSetting(value, _settings.MipmapLevels, v => _settings.MipmapLevels = v); }
    public int BiomeBlend { get => _settings.BiomeBlend; set => SetSetting(value, _settings.BiomeBlend, v => _settings.BiomeBlend = v); }
    public double EntityDistance
    {
        get => _settings.EntityDistance;
        set
        {
            if (Math.Abs(_settings.EntityDistance - value) < 0.001) return;
            _settings.EntityDistance = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(EntityDistanceDisplay));
        }
    }
    public string EntityDistanceDisplay => _settings.EntityDistance.ToString("0.00");

    public bool ViewBobbing { get => _settings.ViewBobbing; set => SetSetting(value, _settings.ViewBobbing, v => _settings.ViewBobbing = v); }
    public int GuiScale { get => _settings.GuiScale; set => SetSetting(value, _settings.GuiScale, v => _settings.GuiScale = v); }
    public double MasterVolume { get => _settings.MasterVolume; set => SetSetting(value, _settings.MasterVolume, v => _settings.MasterVolume = v); }

    // ---------- window ----------
    public int WindowWidth
    {
        get => _settings.WindowWidth;
        set { if (_settings.WindowWidth == value) return; _settings.WindowWidth = value; this.RaisePropertyChanged(); this.RaisePropertyChanged(nameof(ResolutionBadge)); }
    }
    public int WindowHeight
    {
        get => _settings.WindowHeight;
        set { if (_settings.WindowHeight == value) return; _settings.WindowHeight = value; this.RaisePropertyChanged(); this.RaisePropertyChanged(nameof(ResolutionBadge)); }
    }
    public bool Fullscreen
    {
        get => _settings.Fullscreen;
        set { if (_settings.Fullscreen == value) return; _settings.Fullscreen = value; this.RaisePropertyChanged(); this.RaisePropertyChanged(nameof(ResolutionBadge)); }
    }

    private int _resolutionPresetIndex;
    public int ResolutionPresetIndex
    {
        get => _resolutionPresetIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _resolutionPresetIndex, value);
            (int w, int h)? r = value switch
            {
                1 => (1280, 720),
                2 => (1366, 768),
                3 => (1600, 900),
                4 => (1920, 1080),
                5 => (854, 480),
                _ => null
            };
            if (r is not null)
            {
                WindowWidth = r.Value.w;
                WindowHeight = r.Value.h;
            }
        }
    }

    // ---------- OptiFine ----------
    public bool InstallOptifine { get => _settings.InstallOptifine; set => SetSetting(value, _settings.InstallOptifine, v => _settings.InstallOptifine = v); }
    public bool OfFastRender { get => _settings.OfFastRender; set => SetSetting(value, _settings.OfFastRender, v => _settings.OfFastRender = v); }
    public bool OfFastMath { get => _settings.OfFastMath; set => SetSetting(value, _settings.OfFastMath, v => _settings.OfFastMath = v); }
    public bool OfSmartAnimations { get => _settings.OfSmartAnimations; set => SetSetting(value, _settings.OfSmartAnimations, v => _settings.OfSmartAnimations = v); }
    public bool OfDynamicFps { get => _settings.OfDynamicFps; set => SetSetting(value, _settings.OfDynamicFps, v => _settings.OfDynamicFps = v); }
    public bool OfLazyChunkLoading { get => _settings.OfLazyChunkLoading; set => SetSetting(value, _settings.OfLazyChunkLoading, v => _settings.OfLazyChunkLoading = v); }
    public bool OfRenderRegions { get => _settings.OfRenderRegions; set => SetSetting(value, _settings.OfRenderRegions, v => _settings.OfRenderRegions = v); }
    public bool OfShowFps { get => _settings.OfShowFps; set => SetSetting(value, _settings.OfShowFps, v => _settings.OfShowFps = v); }

    public bool OfAnimatedWater { get => _settings.OfAnimatedWater; set => SetSetting(value, _settings.OfAnimatedWater, v => _settings.OfAnimatedWater = v); }
    public bool OfAnimatedLava { get => _settings.OfAnimatedLava; set => SetSetting(value, _settings.OfAnimatedLava, v => _settings.OfAnimatedLava = v); }
    public bool OfAnimatedFire { get => _settings.OfAnimatedFire; set => SetSetting(value, _settings.OfAnimatedFire, v => _settings.OfAnimatedFire = v); }
    public bool OfAnimatedPortal { get => _settings.OfAnimatedPortal; set => SetSetting(value, _settings.OfAnimatedPortal, v => _settings.OfAnimatedPortal = v); }
    public bool OfAnimatedRedstone { get => _settings.OfAnimatedRedstone; set => SetSetting(value, _settings.OfAnimatedRedstone, v => _settings.OfAnimatedRedstone = v); }
    public bool OfAnimatedExplosion { get => _settings.OfAnimatedExplosion; set => SetSetting(value, _settings.OfAnimatedExplosion, v => _settings.OfAnimatedExplosion = v); }
    public bool OfAnimatedTextures { get => _settings.OfAnimatedTextures; set => SetSetting(value, _settings.OfAnimatedTextures, v => _settings.OfAnimatedTextures = v); }

    private static readonly int[] AaLevels = { 0, 2, 4, 8, 16 };
    public int OfAaLevelIndex
    {
        get => Array.IndexOf(AaLevels, _settings.OfAaLevel) is var i && i >= 0 ? i : 0;
        set { if (value < 0 || value >= AaLevels.Length) return; _settings.OfAaLevel = AaLevels[value]; this.RaisePropertyChanged(); }
    }
    public int OfAfLevelIndex
    {
        get => Array.IndexOf(AaLevels, _settings.OfAfLevel) is var i && i >= 0 ? i : 0;
        set { if (value < 0 || value >= AaLevels.Length) return; _settings.OfAfLevel = AaLevels[value]; this.RaisePropertyChanged(); }
    }
    public int OfChunkUpdatesIndex
    {
        get => Math.Clamp(_settings.OfChunkUpdates - 1, 0, 4);
        set { _settings.OfChunkUpdates = value + 1; this.RaisePropertyChanged(); }
    }

    // ---------- Java / JVM ----------
    public string CustomJavaPath
    {
        get => _settings.CustomJavaPath;
        set
        {
            if (_settings.CustomJavaPath == value) return;
            _settings.CustomJavaPath = value ?? "";
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(ResolvedJavaPath));
        }
    }
    public string ResolvedJavaPath =>
        string.IsNullOrWhiteSpace(_settings.CustomJavaPath)
            ? "Авто: " + (File.Exists(Paths.JavaExe) ? Paths.JavaExe : "будет загружена Adoptium Temurin JRE 8u")
            : "Используется: " + _settings.CustomJavaPath;

    public bool UseOptimizedFlags { get => _settings.UseOptimizedFlags; set => SetSetting(value, _settings.UseOptimizedFlags, v => _settings.UseOptimizedFlags = v); }
    public string CustomJvmArgs { get => _settings.CustomJvmArgs; set => SetSetting(value ?? "", _settings.CustomJvmArgs, v => _settings.CustomJvmArgs = v); }
    public string PreLaunchCommand { get => _settings.PreLaunchCommand; set => SetSetting(value ?? "", _settings.PreLaunchCommand, v => _settings.PreLaunchCommand = v); }
    public int PostLaunchAction { get => _settings.PostLaunchAction; set => SetSetting(value, _settings.PostLaunchAction, v => _settings.PostLaunchAction = v); }

    // ---------- Servers ----------
    public string AutoConnectServer { get => _settings.AutoConnectServer; set => SetSetting(value ?? "", _settings.AutoConnectServer, v => _settings.AutoConnectServer = v); }

    public ObservableCollection<string> ServerSuggestions { get; }

    // ---------- system info ----------
    public string OsName => "ОС: " + SystemInfo.OsName;
    public string ArchInfo => "Архитектура: " + SystemInfo.Arch;
    public string RamInfo => SystemInfo.TotalRamMb > 0 ? $"Память: {SystemInfo.TotalRamMb} МБ" : "Память: неизвестно";

    // ---------- play state ----------
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanPlay));
            this.RaisePropertyChanged(nameof(PlayButtonText));
        }
    }

    private string _statusText = "Готово.";
    public string StatusText { get => _statusText; set => this.RaiseAndSetIfChanged(ref _statusText, value); }

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set => this.RaiseAndSetIfChanged(ref _progressPercent, value); }

    private bool _isIndeterminate;
    public bool IsIndeterminate { get => _isIndeterminate; set => this.RaiseAndSetIfChanged(ref _isIndeterminate, value); }

    public bool CanPlay => !IsBusy && !string.IsNullOrWhiteSpace(_settings.Nickname);
    public string PlayButtonText => IsBusy ? "Установка…" : "Играть";

    private string _onlineStatusText = "Подключение…";
    public string OnlineStatusText { get => _onlineStatusText; set => this.RaiseAndSetIfChanged(ref _onlineStatusText, value); }

    private string _authStatus = "";
    public string AuthStatus { get => _authStatus; set => this.RaiseAndSetIfChanged(ref _authStatus, value); }

    // ---------- log ----------
    private string _logText = "";
    public string LogText { get => _logText; set => this.RaiseAndSetIfChanged(ref _logText, value); }

    // ---------- commands ----------
    public ICommand PlayCommand { get; }
    public ICommand MicrosoftLoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand PickJavaCommand { get; }
    public ICommand OpenGameDirCommand { get; }
    public ICommand OpenModsDirCommand { get; }
    public ICommand OpenScreenshotsDirCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand RefreshLogCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand PickServerCommand { get; }

    // ---- 0.3 commands ----
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenLatestReleaseCommand { get; }
    public ICommand ScanCrashesCommand { get; }
    public ICommand OpenCrashCommand { get; }
    public ICommand RefreshContentCommand { get; }
    public ICommand ToggleContentCommand { get; }
    public ICommand DeleteContentCommand { get; }
    public ICommand ImportModCommand { get; }
    public ICommand ImportShaderCommand { get; }
    public ICommand ImportResourcepackCommand { get; }
    public ICommand InstallPerformancePackCommand { get; }
    public ICommand DownloadShaderCommand { get; }
    public ICommand RefreshBackupsCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand DeleteBackupCommand { get; }
    public ICommand OpenBackupsFolderCommand { get; }
    public ICommand RefreshScreenshotsCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }
    public ICommand RefreshLeaderboardCommand { get; }
    public ICommand RefreshNewsCommand { get; }
    public ICommand OpenScreenshotCommand { get; }

    // ---------- helpers ----------
    private void SetSetting<T>(T newValue, T currentValue, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? prop = null)
    {
        if (EqualityComparer<T>.Default.Equals(newValue, currentValue)) return;
        apply(newValue);
        this.RaisePropertyChanged(prop);
    }

    private void RaiseAllSettingsChanged()
    {
        var props = new[]
        {
            nameof(RenderDistance), nameof(SimulationDistance), nameof(MaxFps), nameof(MaxFpsDisplay),
            nameof(GraphicsMode), nameof(Particles), nameof(SmoothLighting),
            nameof(Clouds), nameof(EntityShadows), nameof(VSync), nameof(MipmapLevels), nameof(BiomeBlend),
            nameof(EntityDistance), nameof(EntityDistanceDisplay), nameof(ViewBobbing), nameof(GuiScale), nameof(MasterVolume),
            nameof(OfFastRender), nameof(OfFastMath), nameof(OfSmartAnimations), nameof(OfDynamicFps),
            nameof(OfLazyChunkLoading), nameof(OfRenderRegions), nameof(OfShowFps),
            nameof(OfAnimatedWater), nameof(OfAnimatedLava), nameof(OfAnimatedFire), nameof(OfAnimatedPortal),
            nameof(OfAnimatedRedstone), nameof(OfAnimatedExplosion), nameof(OfAnimatedTextures),
            nameof(OfAaLevelIndex), nameof(OfAfLevelIndex), nameof(OfChunkUpdatesIndex),
            nameof(FpsProfileDescription), nameof(ProfileBadge),
        };
        foreach (var p in props) this.RaisePropertyChanged(p);
    }

    private void PickServer(string s)
    {
        if (!string.IsNullOrWhiteSpace(s)) AutoConnectServer = s;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLogger.Error("OpenFolder failed", ex);
        }
    }

    private async Task PickJavaAsync()
    {
        if (_owner is null) return;
        var sp = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (sp is null) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите javaw.exe или java",
            AllowMultiple = false
        });
        if (files.Count > 0) CustomJavaPath = files[0].Path.LocalPath;
    }

    private async Task ClearCacheAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(Paths.AssetsDir)) Directory.Delete(Paths.AssetsDir, true);
                if (Directory.Exists(Paths.NativesDir)) Directory.Delete(Paths.NativesDir, true);
                AppLogger.Info("Cache cleared by user");
            }
            catch (Exception ex)
            {
                AppLogger.Error("ClearCache failed", ex);
            }
        });
        StatusText = "Кэш ассетов очищен.";
        RefreshLog();
    }

    private void RefreshLog()
    {
        try
        {
            var path = Paths.LogFile;
            if (!File.Exists(path)) { LogText = "(лог пуст)"; return; }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd();
            // Показываем последние ~400 строк, чтобы UI не лагал.
            var lines = all.Split('\n');
            LogText = lines.Length > 400 ? string.Join("\n", lines[^400..]) : all;
        }
        catch (Exception ex)
        {
            LogText = "Не удалось прочитать лог: " + ex.Message;
        }
    }

    private void ClearLog()
    {
        try
        {
            if (File.Exists(Paths.LogFile)) File.WriteAllText(Paths.LogFile, "");
            RefreshLog();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ClearLog failed", ex);
        }
    }

    // ---------- play ----------
    private async Task PlayAsync()
    {
        IsBusy = true;
        try
        {
            _settings.Save();
            StatusText = "Подготовка…";
            ProgressPercent = 0;

            // Pre-launch
            if (!string.IsNullOrWhiteSpace(_settings.PreLaunchCommand))
            {
                try
                {
                    var sh = OperatingSystem.IsWindows() ? "cmd" : "/bin/sh";
                    var args = OperatingSystem.IsWindows() ? "/c " + _settings.PreLaunchCommand : "-c \"" + _settings.PreLaunchCommand.Replace("\"", "\\\"") + "\"";
                    var psi = new ProcessStartInfo(sh, args) { UseShellExecute = false, CreateNoWindow = true };
                    Process.Start(psi)?.WaitForExit(15_000);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Pre-launch failed: " + ex.Message);
                }
            }

            var progress = new Progress<DownloadProgress>(p =>
            {
                StatusText = p.Status;
                if (p.Total > 0) ProgressPercent = p.Percent;
            });

            // 1) Java
            var java = !string.IsNullOrWhiteSpace(_settings.CustomJavaPath) && File.Exists(_settings.CustomJavaPath)
                ? _settings.CustomJavaPath
                : await JavaManager.EnsureJavaAsync(progress).ConfigureAwait(false);

            // 2) Vanilla
            var dl = new MinecraftDownloader("1.16.5");
            await dl.InstallAsync(progress).ConfigureAwait(false);

            // 3) OptiFine
            string? optifineId = null;
            if (_settings.InstallOptifine)
            {
                try
                {
                    await OptifineInstaller.InstallAsync(java, progress).ConfigureAwait(false);
                    optifineId = OptifineInstaller.OptifineVersionDir;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("OptiFine install failed", ex);
                    StatusText = "OptiFine не установился (продолжаем без него).";
                }
            }

            // 4) Auth
            AuthAccount account;
            if (_settings.AuthMode == 1)
            {
                account = AuthService.LoadSaved() ?? AuthService.Offline(_settings.Nickname);
                if (account.Type != "msa")
                {
                    StatusText = "Войдите через Microsoft на вкладке «Профиль».";
                    return;
                }
            }
            else
            {
                account = AuthService.Offline(_settings.Nickname);
            }
            AuthService.Save(account);

            // 5) Launch
            var w = Math.Max(320, _settings.WindowWidth);
            var h = Math.Max(240, _settings.WindowHeight);

            var opts = new LaunchOptions(
                VanillaVersionId: "1.16.5",
                OptifineVersionId: optifineId,
                Username: account.Name,
                Uuid: account.Uuid,
                AccessToken: account.AccessToken,
                UserType: account.Type == "msa" ? "msa" : "legacy",
                RamMb: _settings.RamMb,
                WindowWidth: w,
                WindowHeight: h,
                Fullscreen: _settings.Fullscreen,
                UseOptimizedJvm: _settings.UseOptimizedFlags,
                CustomJvmArgs: _settings.CustomJvmArgs,
                Settings: _settings);

            StatusText = "Запуск Minecraft…";
            var startedAt = DateTime.UtcNow;
            var p = await GameLauncher.LaunchAsync(java, opts).ConfigureAwait(false);
            StatusText = "Игра запущена.";
            ProgressPercent = 100;

            // Discord RPC: «Играет в Minecraft»
            try
            {
                if (DiscordRpcService.Enabled)
                {
                    var detail = string.IsNullOrWhiteSpace(_settings.AutoConnectServer)
                        ? "Single-player"
                        : "Сервер: " + _settings.AutoConnectServer;
                    _ = DiscordRpcService.SetActivityAsync(detail, $"Профиль: {ProfileBadge}");
                }
            }
            catch { }

            // Запоминаем время старта в фоне; когда процесс закончит — запишем в stats.
            _ = Task.Run(async () =>
            {
                try
                {
                    await p.WaitForExitAsync().ConfigureAwait(false);
                    var dur = DateTime.UtcNow - startedAt;
                    PlayerStatsService.RecordSession(dur, _settings.AutoConnectServer);
                    await Dispatcher.UIThread.InvokeAsync(UpdatePlayerStats);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Stats record failed: " + ex.Message);
                }
            });

            // post-launch
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_owner is null) return;
                if (_settings.PostLaunchAction == 1) _owner.WindowState = WindowState.Minimized;
                if (_settings.PostLaunchAction == 2)
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
                        life.Shutdown();
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Play failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MicrosoftLoginAsync()
    {
        try
        {
            AuthStatus = "Запрашиваем код Microsoft…";
            var code = await AuthService.StartDeviceCodeAsync().ConfigureAwait(false);
            AuthStatus = $"Откройте {code.verification_uri} и введите код: {code.user_code}";
            try
            {
                var psi = new ProcessStartInfo(code.verification_uri) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch { }

            var account = await AuthService.PollDeviceTokenAsync(code).ConfigureAwait(false);
            AuthService.Save(account);
            AuthStatus = $"Вход выполнен: {account.Name}";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Nickname = account.Name;
                AuthModeIndex = 1;
            });
        }
        catch (Exception ex)
        {
            AuthStatus = "Ошибка входа: " + ex.Message;
            AppLogger.Error("Microsoft auth failed", ex);
        }
    }

    private void Logout()
    {
        try
        {
            if (File.Exists(Paths.AccountsFile)) File.Delete(Paths.AccountsFile);
            AuthStatus = "Аккаунт сброшен.";
            AuthModeIndex = 0;
        }
        catch { }
    }

    private async Task StartOnlineLoopAsync()
    {
        while (true)
        {
            try
            {
                var n = await OnlineService.PingAsync(_settings.Nickname).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    OnlineStatusText = n >= 0 ? $"Онлайн: {n}" : "Оффлайн");
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
    }

    // ============================================================================
    //                       0.3 features (content/community)
    // ============================================================================

    // ---------- update banner ----------
    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; set => this.RaiseAndSetIfChanged(ref _updateAvailable, value); }

    private string _updateBannerText = "";
    public string UpdateBannerText { get => _updateBannerText; set => this.RaiseAndSetIfChanged(ref _updateBannerText, value); }

    private string _latestReleaseUrl = "https://github.com/prov50686-ops/pclun/releases/latest";
    public string LatestReleaseUrl { get => _latestReleaseUrl; set => this.RaiseAndSetIfChanged(ref _latestReleaseUrl, value); }

    public string CurrentLauncherVersion => UpdateService.CurrentVersion;

    private async Task CheckUpdateAsync()
    {
        StatusText = "Проверка обновлений…";
        var info = await UpdateService.CheckAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (info is null)
            {
                StatusText = "Не удалось проверить обновления.";
                return;
            }
            LatestReleaseUrl = info.Url;
            if (info.IsNewer)
            {
                UpdateAvailable = true;
                UpdateBannerText = $"Доступно обновление: {info.LatestVersion} (у вас {info.CurrentVersion})";
                StatusText = UpdateBannerText;
            }
            else
            {
                UpdateAvailable = false;
                StatusText = $"Установлена последняя версия ({info.CurrentVersion}).";
            }
        });
    }

    private void OpenLatestRelease() => UpdateService.OpenInBrowser(LatestReleaseUrl);

    // ---------- crash analyzer ----------
    public ObservableCollection<CrashAnalyzer.CrashEntry> CrashEntries { get; } = new();

    private string _crashStatus = "Нажмите «Сканировать», чтобы проверить crash-reports/.";
    public string CrashStatus { get => _crashStatus; set => this.RaiseAndSetIfChanged(ref _crashStatus, value); }

    private void ScanCrashes()
    {
        CrashEntries.Clear();
        var entries = CrashAnalyzer.ScanRecent();
        foreach (var e in entries) CrashEntries.Add(e);
        CrashStatus = entries.Count == 0
            ? "Крашей не найдено. Можно играть."
            : $"Найдено {entries.Count} краш-отчётов. Самый свежий: {entries[0].Timestamp:yyyy-MM-dd HH:mm}.";
    }

    private void OpenCrash(CrashAnalyzer.CrashEntry? entry)
    {
        if (entry is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) { AppLogger.Warn("OpenCrash failed: " + ex.Message); }
    }

    // ---------- content (mods/shaders/RP) ----------
    public ObservableCollection<ContentItem> ModItems { get; } = new();
    public ObservableCollection<ContentItem> ShaderItems { get; } = new();
    public ObservableCollection<ContentItem> ResourcePackItems { get; } = new();

    private void RefreshContent()
    {
        ReloadInto(ModItems, ContentManager.List(ContentKind.Mods));
        ReloadInto(ShaderItems, ContentManager.List(ContentKind.Shaderpacks));
        ReloadInto(ResourcePackItems, ContentManager.List(ContentKind.Resourcepacks));
    }

    private static void ReloadInto<T>(ObservableCollection<T> coll, IList<T> items)
    {
        coll.Clear();
        foreach (var i in items) coll.Add(i);
    }

    private void ToggleContent(ContentItem? item)
    {
        if (item is null) return;
        try
        {
            ContentManager.Toggle(item);
            RefreshContent();
            StatusText = item.Enabled ? $"Отключено: {item.Name}" : $"Включено: {item.Name}";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Toggle content failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    private void DeleteContent(ContentItem? item)
    {
        if (item is null) return;
        try
        {
            ContentManager.Delete(item);
            RefreshContent();
            StatusText = $"Удалено: {item.Name}";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Delete content failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    private async Task ImportContentAsync(ContentKind kind)
    {
        if (_owner is null) return;
        var sp = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (sp is null) return;

        var (title, ext) = kind switch
        {
            ContentKind.Mods => ("Выберите mod (.jar)", new[] { "jar" }),
            ContentKind.Shaderpacks => ("Выберите шейдер (.zip)", new[] { "zip", "jar" }),
            ContentKind.Resourcepacks => ("Выберите resourcepack (.zip)", new[] { "zip" }),
            _ => ("Файл", new[] { "*" })
        };

        var picked = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Поддерживаемые") { Patterns = ext.Select(e => "*." + e).ToList() }
            }
        });
        if (picked is null || picked.Count == 0) return;

        try
        {
            foreach (var f in picked)
                ContentManager.ImportFile(f.Path.LocalPath, kind);
            RefreshContent();
            StatusText = $"Добавлено: {picked.Count}";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Import content failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    // ---------- performance modpack + shaders catalog ----------
    public IReadOnlyList<ModInfo> PerformanceMods => PerformanceModpack.Mods;
    public IReadOnlyList<ShaderpackInfo> ShaderCatalog => ShaderpackCatalog.Items;

    private async Task InstallPerformancePackAsync()
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<DownloadProgress>(p => StatusText = p.Status);
            await PerformanceModpack.DownloadModsAsync(progress).ConfigureAwait(false);
            var forge = await PerformanceModpack.EnsureForgeInstallerAsync(progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = "Моды скачаны. Forge installer лежит в temp — двойной клик чтобы установить.";
                RefreshContent();
            });
            try
            {
                Process.Start(new ProcessStartInfo(forge) { UseShellExecute = true });
            }
            catch { /* Linux/macOS — пользователь откроет вручную */ }
        }
        catch (Exception ex)
        {
            AppLogger.Error("InstallPerformancePack failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DownloadShaderAsync(ShaderpackInfo? pack)
    {
        if (pack is null) return;
        IsBusy = true;
        try
        {
            var progress = new Progress<DownloadProgress>(p => StatusText = p.Status);
            await ShaderpackCatalog.DownloadAsync(pack, progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(RefreshContent);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Download shader failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ---------- backups ----------
    public ObservableCollection<BackupInfo> Backups { get; } = new();

    private void RefreshBackups() => ReloadInto(Backups, BackupService.List());

    private async Task CreateBackupAsync()
    {
        IsBusy = true;
        StatusText = "Создаём бэкап миров…";
        try
        {
            var info = await BackupService.CreateAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshBackups();
                StatusText = $"Бэкап создан: {info.FileName} ({info.SizeBytes / 1024} КБ)";
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("CreateBackup failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup is null) return;
        IsBusy = true;
        StatusText = "Восстанавливаем бэкап (текущие миры будут сохранены автоматически)…";
        try
        {
            await BackupService.RestoreAsync(backup, replaceExisting: true).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshBackups();
                StatusText = "Бэкап восстановлен.";
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("RestoreBackup failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void DeleteBackup(BackupInfo? backup)
    {
        if (backup is null) return;
        try
        {
            BackupService.Delete(backup);
            RefreshBackups();
            StatusText = "Бэкап удалён.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("DeleteBackup failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    // ---------- screenshots gallery ----------
    public ObservableCollection<string> Screenshots { get; } = new();

    private void RefreshScreenshots()
    {
        Screenshots.Clear();
        var dir = Path.Combine(Paths.GameDir, "screenshots");
        if (!Directory.Exists(dir)) return;
        var files = new DirectoryInfo(dir)
            .GetFiles("*.png")
            .OrderByDescending(f => f.LastWriteTime)
            .Take(60);
        foreach (var f in files) Screenshots.Add(f.FullName);
    }

    private void OpenScreenshot(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { AppLogger.Warn("OpenScreenshot failed: " + ex.Message); }
    }

    // ---------- settings export/import ----------
    private async Task ExportSettingsAsync()
    {
        if (_owner is null) return;
        var sp = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (sp is null) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить настройки",
            SuggestedFileName = $"pclun-settings-{DateTime.Now:yyyyMMdd}.json",
            DefaultExtension = "json"
        });
        if (file is null) return;
        try
        {
            SettingsExport.Export(_settings, file.Path.LocalPath);
            StatusText = "Настройки экспортированы: " + file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportSettings failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    private async Task ImportSettingsAsync()
    {
        if (_owner is null) return;
        var sp = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (sp is null) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Загрузить настройки",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        if (files is null || files.Count == 0) return;
        try
        {
            var loaded = SettingsExport.Import(files[0].Path.LocalPath);
            // Перезагрузим LauncherSettings — простейший способ: применить значения через property-setters,
            // что и сохранит/обновит UI. Для краткости — попросим юзера перезапустить.
            StatusText = "Настройки загружены. Перезапустите лаунчер для применения.";
            AppLogger.Info("Settings imported, will be active after restart.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("ImportSettings failed", ex);
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    // ---------- player stats ----------
    private string _playerStatsLine = "Часов: 0 · Сессий: 0";
    public string PlayerStatsLine { get => _playerStatsLine; set => this.RaiseAndSetIfChanged(ref _playerStatsLine, value); }

    private string _lastServerLine = "—";
    public string LastServerLine { get => _lastServerLine; set => this.RaiseAndSetIfChanged(ref _lastServerLine, value); }

    private void UpdatePlayerStats()
    {
        var s = PlayerStatsService.Load();
        var fps = PlayerStatsService.AvgFpsFromLatestLog();
        PlayerStatsLine = $"Часов: {s.Hours} · Сессий: {s.Sessions} · Самая длинная: {TimeSpan.FromSeconds(s.LongestSessionSeconds).Hours}ч{TimeSpan.FromSeconds(s.LongestSessionSeconds).Minutes:00}м"
                          + (fps is not null ? $" · ~FPS: {fps:0}" : "");
        LastServerLine = string.IsNullOrWhiteSpace(s.LastServer) ? "—" : s.LastServer;
    }

    // ---------- skin viewer ----------
    private string _skinAvatarUrl = "";
    public string SkinAvatarUrl { get => _skinAvatarUrl; set => this.RaiseAndSetIfChanged(ref _skinAvatarUrl, value); }

    private string _skinFullBodyUrl = "";
    public string SkinFullBodyUrl { get => _skinFullBodyUrl; set => this.RaiseAndSetIfChanged(ref _skinFullBodyUrl, value); }

    private void UpdateSkinUrls()
    {
        SkinAvatarUrl = SkinService.AvatarUrl(_settings.Nickname);
        SkinFullBodyUrl = SkinService.FullBodyUrl(_settings.Nickname);
    }

    // ---------- news + leaderboard ----------
    public ObservableCollection<RemoteCatalog.NewsItem> NewsItems { get; } = new();
    public ObservableCollection<RemoteCatalog.LeaderboardItem> LeaderboardItems { get; } = new();

    private string _statsLine = "—";
    public string StatsLine { get => _statsLine; set => this.RaiseAndSetIfChanged(ref _statsLine, value); }

    private async Task RefreshNewsAsync()
    {
        var items = await RemoteCatalog.FetchNewsAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            NewsItems.Clear();
            foreach (var i in items) NewsItems.Add(i);
        });
    }

    private async Task RefreshLeaderboardAsync()
    {
        var items = await RemoteCatalog.FetchLeaderboardAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LeaderboardItems.Clear();
            foreach (var i in items) LeaderboardItems.Add(i);
        });
    }

    private async Task RefreshStatsAsync()
    {
        var s = await RemoteCatalog.FetchStatsAsync().ConfigureAwait(false);
        if (s is null) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
            StatsLine = $"Сейчас в игре: {s.Online} · сегодня уник.: {s.Today} · пик: {s.Peak} · всего: {s.Total}");
    }

    private async Task InitExtrasAsync()
    {
        // Проверка обновлений в фоне.
        try
        {
            var info = await UpdateService.CheckAsync().ConfigureAwait(false);
            if (info is { IsNewer: true })
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateAvailable = true;
                    UpdateBannerText = $"Доступно обновление: {info.LatestVersion} (у вас {info.CurrentVersion})";
                    LatestReleaseUrl = info.Url;
                });
            }
        }
        catch { }

        // Подгрузка серверов из бэкенда (с фоллбэком на захардкоженный список).
        try
        {
            var remote = await RemoteCatalog.FetchServersAsync().ConfigureAwait(false);
            if (remote.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ServerSuggestions.Clear();
                    foreach (var s in remote) ServerSuggestions.Add(s.Address);
                    if (!ServerSuggestions.Contains("localhost:25565"))
                        ServerSuggestions.Add("localhost:25565");
                });
            }
        }
        catch { }

        await RefreshNewsAsync().ConfigureAwait(false);
        await RefreshStatsAsync().ConfigureAwait(false);
        await RefreshLeaderboardAsync().ConfigureAwait(false);

        // Discord RPC — best-effort.
        try
        {
            if (await DiscordRpcService.ConnectAsync().ConfigureAwait(false))
            {
                await DiscordRpcService.SetActivityAsync(
                    state: $"Профиль: {ProfileBadge}",
                    details: "В лаунчере PcLun by MrDomik").ConfigureAwait(false);
            }
        }
        catch { }
    }
}
