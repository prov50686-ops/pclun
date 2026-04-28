using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using PcLun.Services;
using ReactiveUI;

namespace PcLun.ViewModels;

public class MainViewModel : ReactiveObject
{
    private readonly Window? _owner;

    public MainViewModel() : this(null) { }

    public MainViewModel(Window? owner)
    {
        _owner = owner;
        var saved = AuthService.LoadSaved();
        if (saved is not null)
        {
            _nickname = saved.Name;
            _authModeIndex = saved.Type == "msa" ? 1 : 0;
        }
        else
        {
            _nickname = "Player" + new Random().Next(100, 999);
        }

        _ramMb = SystemInfo.RecommendedRamMb();
        UpdateRamHint();

        PlayCommand = ReactiveCommand.CreateFromTask(PlayAsync, this.WhenAnyValue(x => x.CanPlay));
        MicrosoftLoginCommand = ReactiveCommand.CreateFromTask(MicrosoftLoginAsync);
        LogoutCommand = ReactiveCommand.Create(Logout);

        // онлайн-счётчик
        _ = StartOnlineLoopAsync();
    }

    // ---------- tabs ----------
    private bool _isHomeTab = true, _isSettingsTab, _isAccountTab, _isAboutTab;
    public bool IsHomeTab { get => _isHomeTab; set => this.RaiseAndSetIfChanged(ref _isHomeTab, value); }
    public bool IsSettingsTab { get => _isSettingsTab; set => this.RaiseAndSetIfChanged(ref _isSettingsTab, value); }
    public bool IsAccountTab { get => _isAccountTab; set => this.RaiseAndSetIfChanged(ref _isAccountTab, value); }
    public bool IsAboutTab { get => _isAboutTab; set => this.RaiseAndSetIfChanged(ref _isAboutTab, value); }

    // ---------- account ----------
    private string _nickname = "";
    public string Nickname { get => _nickname; set { this.RaiseAndSetIfChanged(ref _nickname, value); this.RaisePropertyChanged(nameof(UserDisplayName)); this.RaisePropertyChanged(nameof(NicknameInitial)); } }

    private int _authModeIndex; // 0 = offline, 1 = MS
    public int AuthModeIndex { get => _authModeIndex; set { this.RaiseAndSetIfChanged(ref _authModeIndex, value); this.RaisePropertyChanged(nameof(AuthModeText)); } }

    public string AuthModeText => AuthModeIndex == 1 ? "Microsoft (премиум)" : "Офлайн";
    public string UserDisplayName => string.IsNullOrWhiteSpace(Nickname) ? "Гость" : Nickname.Trim();
    public string NicknameInitial => UserDisplayName.Length > 0 ? UserDisplayName[..1].ToUpper() : "?";

    // ---------- ram ----------
    private int _ramMb;
    public int RamMb { get => _ramMb; set { this.RaiseAndSetIfChanged(ref _ramMb, value); UpdateRamHint(); } }

    private string _ramHint = "";
    public string RamHint { get => _ramHint; set => this.RaiseAndSetIfChanged(ref _ramHint, value); }

    private void UpdateRamHint()
    {
        var rec = SystemInfo.RecommendedRamMb();
        var total = SystemInfo.TotalRamMb;
        RamHint = total > 0
            ? $"Системная RAM: {total} МБ. Рекомендуем: {rec} МБ. Сейчас: {RamMb} МБ."
            : $"Рекомендуем для слабых ПК: 1024-2048 МБ. Сейчас: {RamMb} МБ.";
    }

    // ---------- settings ----------
    private int _fpsProfileIndex = 0; // 0 = ultra fps
    public int FpsProfileIndex { get => _fpsProfileIndex; set { this.RaiseAndSetIfChanged(ref _fpsProfileIndex, value); this.RaisePropertyChanged(nameof(FpsProfileDescription)); } }

    public string FpsProfileDescription => FpsProfileIndex switch
    {
        0 => "Render distance 4, минимум частиц, выкл. облака, Fast Render, Dynamic FPS, Smart Animations off. Цель — максимум FPS на слабом ПК.",
        1 => "Render distance 8, средние настройки, OptiFine оптимизации сохранены, но с большим качеством картинки.",
        2 => "Render distance 12, Fancy graphics, без агрессивной оптимизации. Только для мощных ПК.",
        _ => ""
    };

    private bool _useOptimizedFlags = true;
    public bool UseOptimizedFlags { get => _useOptimizedFlags; set => this.RaiseAndSetIfChanged(ref _useOptimizedFlags, value); }

    private bool _installOptifine = true;
    public bool InstallOptifine { get => _installOptifine; set => this.RaiseAndSetIfChanged(ref _installOptifine, value); }

    public string LauncherDir => Paths.LauncherRoot;
    public string GameDir => Paths.GameDir;
    public string JavaPath => Paths.JavaExe;

    private string _windowWidth = "1280";
    public string WindowWidth { get => _windowWidth; set => this.RaiseAndSetIfChanged(ref _windowWidth, value); }

    private string _windowHeight = "720";
    public string WindowHeight { get => _windowHeight; set => this.RaiseAndSetIfChanged(ref _windowHeight, value); }

    private bool _fullscreen;
    public bool Fullscreen { get => _fullscreen; set => this.RaiseAndSetIfChanged(ref _fullscreen, value); }

    // ---------- system info ----------
    public string OsInfo => "ОС: " + SystemInfo.OsName;
    public string ArchInfo => "Архитектура: " + SystemInfo.Arch;
    public string RamInfo => SystemInfo.TotalRamMb > 0 ? $"Память: {SystemInfo.TotalRamMb} МБ" : "Память: неизвестно";
    public string VersionInfo => "PcLun 0.1.0 — Minecraft 1.16.5 + OptiFine HD U G8";

    // ---------- play state ----------
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { this.RaiseAndSetIfChanged(ref _isBusy, value); this.RaisePropertyChanged(nameof(CanPlay)); this.RaisePropertyChanged(nameof(PlayButtonText)); } }

    private string _statusText = "Готово.";
    public string StatusText { get => _statusText; set => this.RaiseAndSetIfChanged(ref _statusText, value); }

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set => this.RaiseAndSetIfChanged(ref _progressPercent, value); }

    public bool CanPlay => !IsBusy && !string.IsNullOrWhiteSpace(Nickname);
    public string PlayButtonText => IsBusy ? "Установка…" : "Играть";
    public string ReadyDescription => "Версия 1.16.5 с OptiFine, профиль: " + FpsProfileDescription.Split('.')[0];

    private string _onlineStatusText = "Подключение…";
    public string OnlineStatusText { get => _onlineStatusText; set => this.RaiseAndSetIfChanged(ref _onlineStatusText, value); }

    private string _authStatus = "";
    public string AuthStatus { get => _authStatus; set => this.RaiseAndSetIfChanged(ref _authStatus, value); }

    public ICommand PlayCommand { get; }
    public ICommand MicrosoftLoginCommand { get; }
    public ICommand LogoutCommand { get; }

    // ---------- actions ----------
    private async Task PlayAsync()
    {
        IsBusy = true;
        try
        {
            StatusText = "Подготовка…";
            ProgressPercent = 0;

            var progress = new Progress<DownloadProgress>(p =>
            {
                StatusText = p.Status;
                if (p.Total > 0) ProgressPercent = p.Percent;
            });

            // 1) Java
            var java = await JavaManager.EnsureJavaAsync(progress).ConfigureAwait(false);

            // 2) Vanilla 1.16.5
            var dl = new MinecraftDownloader("1.16.5");
            var install = await dl.InstallAsync(progress).ConfigureAwait(false);

            // 3) OptiFine
            string? optifineId = null;
            if (InstallOptifine)
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
            if (AuthModeIndex == 1)
            {
                account = AuthService.LoadSaved() ?? AuthService.Offline(Nickname);
                if (account.Type != "msa")
                {
                    StatusText = "Войдите через Microsoft на вкладке «Аккаунт».";
                    return;
                }
            }
            else
            {
                account = AuthService.Offline(Nickname);
            }
            AuthService.Save(account);

            // 5) Launch
            var profile = (FpsProfile)FpsProfileIndex;
            int.TryParse(WindowWidth, out var w);
            int.TryParse(WindowHeight, out var h);
            if (w < 320) w = 1280;
            if (h < 240) h = 720;

            var opts = new LaunchOptions(
                VanillaVersionId: "1.16.5",
                OptifineVersionId: optifineId,
                Username: account.Name,
                Uuid: account.Uuid,
                AccessToken: account.AccessToken,
                UserType: account.Type == "msa" ? "msa" : "legacy",
                RamMb: RamMb,
                WindowWidth: w,
                WindowHeight: h,
                Fullscreen: Fullscreen,
                UseOptimizedJvm: UseOptimizedFlags,
                Profile: profile);

            StatusText = "Запуск Minecraft…";
            var p = await GameLauncher.LaunchAsync(java, opts).ConfigureAwait(false);
            StatusText = "Игра запущена. Удачной игры!";
            ProgressPercent = 100;
            // не ждём выхода — отдадим управление обратно UI
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

            // открываем браузер
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(code.verification_uri) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
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
            if (System.IO.File.Exists(Paths.AccountsFile))
                System.IO.File.Delete(Paths.AccountsFile);
            AuthStatus = "Аккаунт сброшен.";
        }
        catch { }
    }

    private async Task StartOnlineLoopAsync()
    {
        while (true)
        {
            try
            {
                var n = await OnlineService.PingAsync(UserDisplayName).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    OnlineStatusText = n >= 0 ? $"Онлайн: {n}" : "Оффлайн");
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
    }
}
