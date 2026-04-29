using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PcLun.Services;
using ReactiveUI;

namespace PcLun.ViewModels;

/// <summary>
/// Surface for the "Pro" tab — multi-instance, dynamic versions, Modrinth,
/// server browser, friends/chat/achievements, cloud backup, theme, diagnostics, etc.
/// Lives as a partial of <see cref="MainViewModel"/> to keep the main file readable.
/// </summary>
public partial class MainViewModel
{
    // ---------- Pro tab ----------
    public bool IsProTab { get => ActiveTab == 10; set { if (value) ActiveTab = 10; } }

    // ---------- Instances ----------
    public ObservableCollection<InstanceManager.Instance> Instances { get; } = new();
    public string NewInstanceName { get; set; } = "";
    public string NewInstanceVersion { get; set; } = "1.16.5";
    public string NewInstanceLoader { get; set; } = "forge";

    private InstanceManager.Instance? _activeInstance;
    public InstanceManager.Instance? ActiveInstance
    {
        get => _activeInstance;
        set
        {
            if (_activeInstance == value) return;
            _activeInstance = value;
            this.RaisePropertyChanged();
            if (value is not null)
            {
                _settings.ActiveInstance = value.Name;
                _settings.McVersion = value.McVersion;
                _settings.Loader = value.Loader;
            }
        }
    }

    public ReactiveCommand<Unit, Unit>? CreateInstanceCommand { get; private set; }
    public ReactiveCommand<InstanceManager.Instance?, Unit>? DeleteInstanceCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? RefreshInstancesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? RefreshVersionsCommand { get; private set; }
    public ObservableCollection<string> AvailableMcVersions { get; } = new(VersionManager.PopularPresets);

    // ---------- Modrinth ----------
    public ObservableCollection<ModrinthClient.ProjectHit> ModrinthResults { get; } = new();
    public string ModrinthQuery { get; set; } = "";
    public string ModrinthMcVersion { get; set; } = "1.16.5";
    public string ModrinthLoader { get; set; } = "forge";
    public ReactiveCommand<Unit, Unit>? ModrinthSearchCommand { get; private set; }
    public ReactiveCommand<ModrinthClient.ProjectHit?, Unit>? ModrinthInstallCommand { get; private set; }

    // ---------- Modpack import ----------
    public ReactiveCommand<Unit, Unit>? ImportModpackCommand { get; private set; }

    // ---------- Server browser ----------
    public ObservableCollection<ServerEntry> ServerEntries { get; } = new();
    public string ServerPingInput { get; set; } = "play.hypixel.net";
    public ReactiveCommand<Unit, Unit>? PingServerCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? PingFeaturedCommand { get; private set; }

    public class ServerEntry : ReactiveObject
    {
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Status { get; set; } = "—";
        public string Motd { get; set; } = "";
        public int Online { get; set; }
        public int Max { get; set; }
        public long LatencyMs { get; set; }
        public string Display => $"{Name} · {Address} · {Status}";
    }

    // ---------- Loader installers (Replay, Sodium, Iris, Vivecraft, Fabric) ----------
    public ObservableCollection<LoaderInstallers.InstallerInfo> Installers { get; } =
        new(LoaderInstallers.Catalog);
    public ReactiveCommand<LoaderInstallers.InstallerInfo?, Unit>? InstallExtraCommand { get; private set; }

    // ---------- Cape ----------
    public string CapeStatus { get; private set; } = "Войдите Microsoft Account для смены плаща.";

    // ---------- Friends ----------
    public ObservableCollection<FriendsService.Friend> Friends { get; } = new();
    public string FriendToAdd { get; set; } = "";
    public ReactiveCommand<Unit, Unit>? RefreshFriendsCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? AddFriendCommand { get; private set; }
    public ReactiveCommand<FriendsService.Friend?, Unit>? RemoveFriendCommand { get; private set; }

    // ---------- Chat ----------
    private ChatClient? _chat;
    public ObservableCollection<ChatClient.ChatMessage> ChatMessages { get; } = new();
    public string ChatInput { get; set; } = "";
    public string ChatStatus { get; private set; } = "Не подключено";
    public ReactiveCommand<Unit, Unit>? ConnectChatCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SendChatCommand { get; private set; }

    // ---------- Achievements ----------
    public ObservableCollection<AchievementService.Achievement> Achievements { get; } = new();
    public ReactiveCommand<Unit, Unit>? RefreshAchievementsCommand { get; private set; }

    // ---------- Diagnostics ----------
    public string GpuName { get; private set; } = "—";
    public string GpuWarning { get; private set; } = "";
    public string CpuTemp { get; private set; } = "—";
    public string ModConflictSummary { get; private set; } = "—";
    public string AntiCheatSummary { get; private set; } = "—";
    public ReactiveCommand<Unit, Unit>? RunDiagnosticsCommand { get; private set; }

    // ---------- Cloud backup ----------
    public string CloudBackupStatus { get; private set; } = "—";
    public ReactiveCommand<Unit, Unit>? CloudBackupCommand { get; private set; }

    // ---------- Theme + accent ----------
    public int ThemeMode
    {
        get => _settings.ThemeMode;
        set
        {
            if (_settings.ThemeMode == value) return;
            _settings.ThemeMode = value;
            this.RaisePropertyChanged();
            ApplyThemeFromSettings();
        }
    }
    public string AccentColor
    {
        get => _settings.AccentColor;
        set
        {
            if (_settings.AccentColor == value) return;
            _settings.AccentColor = value;
            this.RaisePropertyChanged();
            ApplyThemeFromSettings();
        }
    }

    // ---------- Toggles ----------
    public bool StreamerModeEnabled
    {
        get => _settings.StreamerMode;
        set { if (_settings.StreamerMode == value) return; _settings.StreamerMode = value; StreamerMode.Enabled = value; this.RaisePropertyChanged(); }
    }
    public bool CompactMode
    {
        get => _settings.CompactMode;
        set { if (_settings.CompactMode == value) return; _settings.CompactMode = value; this.RaisePropertyChanged(); ApplyCompactModeFromSettings(); }
    }
    public bool AutostartWithOs
    {
        get => _settings.AutostartWithOs;
        set { if (_settings.AutostartWithOs == value) return; _settings.AutostartWithOs = value; AutostartService.SetEnabled(value); this.RaisePropertyChanged(); }
    }
    public bool NotificationsEnabled
    {
        get => _settings.NotificationsEnabled;
        set { if (_settings.NotificationsEnabled == value) return; _settings.NotificationsEnabled = value; this.RaisePropertyChanged(); }
    }
    public bool TelemetryEnabled
    {
        get => _settings.TelemetryEnabled;
        set { if (_settings.TelemetryEnabled == value) return; _settings.TelemetryEnabled = value; this.RaisePropertyChanged(); }
    }
    public bool AutoFpsProfile
    {
        get => _settings.AutoFpsProfile;
        set { if (_settings.AutoFpsProfile == value) return; _settings.AutoFpsProfile = value; this.RaisePropertyChanged(); ApplyAutoFpsIfEnabled(); }
    }
    public bool NetworkTunerEnabled
    {
        get => _settings.NetworkTunerEnabled;
        set { if (_settings.NetworkTunerEnabled == value) return; _settings.NetworkTunerEnabled = value; this.RaisePropertyChanged(); }
    }
    public bool DemoteBackgroundProcs
    {
        get => _settings.DemoteBackgroundProcs;
        set { if (_settings.DemoteBackgroundProcs == value) return; _settings.DemoteBackgroundProcs = value; this.RaisePropertyChanged(); }
    }
    public bool BackgroundUpdateEnabled
    {
        get => _settings.BackgroundUpdate;
        set { if (_settings.BackgroundUpdate == value) return; _settings.BackgroundUpdate = value; this.RaisePropertyChanged(); }
    }

    // ---------- Hosts editor ----------
    public ObservableCollection<HostsEditor.Mapping> HostsMappings { get; } = new();
    public string HostsPatchInfo { get; private set; } = "";
    public ReactiveCommand<Unit, Unit>? RefreshHostsCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? StageHostsPatchCommand { get; private set; }

    // ---------- Init for Pro tab (called from main ctor below) ----------
    private void InitProSurface()
    {
        // Apply theme + streamer + autostart on start.
        StreamerMode.Enabled = _settings.StreamerMode;
        ApplyThemeFromSettings();

        CreateInstanceCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrWhiteSpace(NewInstanceName)) return;
            var inst = InstanceManager.Create(NewInstanceName, NewInstanceVersion, NewInstanceLoader);
            Instances.Add(inst);
            ActiveInstance = inst;
            StatusText = $"Создан инстанс {inst.Name}";
        });
        DeleteInstanceCommand = ReactiveCommand.Create<InstanceManager.Instance?>(i =>
        {
            if (i is null || i.IsDefault) return;
            InstanceManager.Delete(i.Name);
            Instances.Remove(i);
            if (Equals(ActiveInstance, i)) ActiveInstance = Instances.FirstOrDefault();
        });
        RefreshInstancesCommand = ReactiveCommand.Create(() =>
        {
            Instances.Clear();
            foreach (var i in InstanceManager.List()) Instances.Add(i);
            ActiveInstance = Instances.FirstOrDefault(i => i.Name == _settings.ActiveInstance) ?? Instances.FirstOrDefault();
        });
        RefreshVersionsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var list = await VersionManager.ListReleasesAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableMcVersions.Clear();
                foreach (var v in list) AvailableMcVersions.Add(v);
            });
        });

        ModrinthSearchCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ModrinthResults.Clear();
            var hits = await ModrinthClient.SearchAsync(ModrinthQuery, ModrinthMcVersion, ModrinthLoader);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var h in hits) ModrinthResults.Add(h);
                StatusText = $"Найдено модов: {hits.Count}";
            });
        });
        ModrinthInstallCommand = ReactiveCommand.CreateFromTask<ModrinthClient.ProjectHit?>(async hit =>
        {
            if (hit is null) return;
            var versions = await ModrinthClient.ListVersionsAsync(hit.Slug, ModrinthMcVersion, ModrinthLoader);
            var primary = versions.FirstOrDefault()?.Files.FirstOrDefault(f => f.Primary)
                          ?? versions.FirstOrDefault()?.Files.FirstOrDefault();
            if (primary is null) { StatusText = "Не найдено файлов для установки"; return; }
            var dir = Path.Combine(GameDirOfActiveInstance(), "mods");
            var ok = await ModrinthClient.DownloadAsync(primary, dir);
            StatusText = ok ? $"Установлено: {primary.Filename}" : "Ошибка скачивания";
            if (ok) RefreshContent();
        });

        ImportModpackCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_owner is null) return;
            var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Modpack") { Patterns = new[] { "*.zip", "*.mrpack" } } },
            });
            if (files.Count == 0) return;
            StatusText = "Импорт модпака…";
            var src = files[0].Path.LocalPath;
            var name = "pack-" + DateTime.Now.Ticks;
            var r = await ModpackImporter.ImportAsync(src, name);
            StatusText = r.Ok
                ? $"Импортирован {r.InstanceName} ({r.ModsExtracted} модов)"
                : "Ошибка импорта: " + r.Error;
            RefreshInstancesCommand?.Execute().Subscribe(_ => { }, _ => { });
        });

        PingServerCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var entry = new ServerEntry { Address = ServerPingInput, Name = ServerPingInput, Status = "ping…" };
            await Dispatcher.UIThread.InvokeAsync(() => ServerEntries.Add(entry));
            var r = await ServerPinger.PingAsync(ServerPingInput);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                entry.Status = r.Ok ? $"{r.LatencyMs}ms · {r.OnlinePlayers}/{r.MaxPlayers}" : ("offline: " + r.Error);
                entry.Motd = r.Motd;
                entry.Online = r.OnlinePlayers;
                entry.Max = r.MaxPlayers;
                entry.LatencyMs = r.LatencyMs;
                this.RaisePropertyChanged(nameof(ServerEntries));
            });
        });
        PingFeaturedCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ServerEntries.Clear();
            foreach (var addr in ServerSuggestions.Take(8))
            {
                var entry = new ServerEntry { Name = addr, Address = addr, Status = "ping…" };
                await Dispatcher.UIThread.InvokeAsync(() => ServerEntries.Add(entry));
                var r = await ServerPinger.PingAsync(addr);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    entry.Status = r.Ok ? $"{r.LatencyMs}ms · {r.OnlinePlayers}/{r.MaxPlayers}" : "offline";
                    entry.Motd = r.Motd;
                });
            }
        });

        InstallExtraCommand = ReactiveCommand.CreateFromTask<LoaderInstallers.InstallerInfo?>(async info =>
        {
            if (info is null) return;
            StatusText = $"Загрузка {info.Name}…";
            var ok = await LoaderInstallers.InstallAsync(info, GameDirOfActiveInstance());
            StatusText = ok ? $"Установлен {info.Name}" : "Ошибка установки";
        });

        RefreshFriendsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var nick = string.IsNullOrEmpty(_settings.FriendOwner) ? Nickname : _settings.FriendOwner;
            var list = await FriendsService.ListAsync(nick);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Friends.Clear();
                foreach (var f in list) Friends.Add(f);
            });
        });
        AddFriendCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (string.IsNullOrWhiteSpace(FriendToAdd)) return;
            var owner = string.IsNullOrEmpty(_settings.FriendOwner) ? Nickname : _settings.FriendOwner;
            _settings.FriendOwner = owner;
            await FriendsService.AddAsync(owner, FriendToAdd.Trim());
            FriendToAdd = "";
            this.RaisePropertyChanged(nameof(FriendToAdd));
            RefreshFriendsCommand?.Execute().Subscribe(_ => { }, _ => { });
            _ = AchievementService.UnlockAsync(owner, "first_friend");
        });
        RemoveFriendCommand = ReactiveCommand.CreateFromTask<FriendsService.Friend?>(async f =>
        {
            if (f is null) return;
            var owner = string.IsNullOrEmpty(_settings.FriendOwner) ? Nickname : _settings.FriendOwner;
            await FriendsService.RemoveAsync(owner, f.Name);
            RefreshFriendsCommand?.Execute().Subscribe(_ => { }, _ => { });
        });

        ConnectChatCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _chat?.Dispose();
            _chat = new ChatClient(OnlineService.BackendUrl, Nickname);
            ChatMessages.Clear();
            // proxy collection
            _chat.Messages.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null) return;
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var m in e.NewItems) if (m is ChatClient.ChatMessage cm) ChatMessages.Add(cm);
                });
            };
            await _chat.ConnectAsync();
            ChatStatus = _chat.IsConnected ? "Подключено" : "Не удалось подключиться";
            this.RaisePropertyChanged(nameof(ChatStatus));
        });
        SendChatCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_chat is null || string.IsNullOrWhiteSpace(ChatInput)) return;
            await _chat.SendAsync(ChatInput.Trim());
            ChatInput = "";
            this.RaisePropertyChanged(nameof(ChatInput));
        });

        RefreshAchievementsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var list = await AchievementService.ListAsync(Nickname);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Achievements.Clear();
                foreach (var a in list) Achievements.Add(a);
            });
        });

        RunDiagnosticsCommand = ReactiveCommand.Create(() =>
        {
            var gpu = Diagnostics.DetectGpu();
            GpuName = gpu.Name;
            GpuWarning = gpu.Warning;
            this.RaisePropertyChanged(nameof(GpuName));
            this.RaisePropertyChanged(nameof(GpuWarning));
            var temp = Diagnostics.ReadCpuTempCelsius();
            CpuTemp = temp.HasValue ? $"{temp.Value:F1} °C" : "недоступно";
            this.RaisePropertyChanged(nameof(CpuTemp));
            var conflicts = ModConflictDetector.Scan(GameDirOfActiveInstance());
            ModConflictSummary = conflicts.Summary;
            this.RaisePropertyChanged(nameof(ModConflictSummary));
            var antiCheat = AntiCheatSelfTest.Run(GameDirOfActiveInstance());
            AntiCheatSummary = antiCheat.Summary;
            this.RaisePropertyChanged(nameof(AntiCheatSummary));
            _ = TelemetryService.ReportGpuAsync(gpu.Name, _settings.TelemetryEnabled);
        });

        CloudBackupCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // Берём последний бэкап из BackupService.
            var backups = BackupService.List();
            var last = backups.FirstOrDefault();
            if (last is null) { CloudBackupStatus = "сначала создай бэкап на вкладке Контент"; this.RaisePropertyChanged(nameof(CloudBackupStatus)); return; }
            var (ok, msg) = await CloudBackupService.UploadAsync(Nickname, last.FullPath);
            CloudBackupStatus = (ok ? "✓ " : "✗ ") + msg;
            this.RaisePropertyChanged(nameof(CloudBackupStatus));
        });

        RefreshHostsCommand = ReactiveCommand.Create(() =>
        {
            HostsMappings.Clear();
            foreach (var m in HostsEditor.Read()) HostsMappings.Add(m);
        });
        StageHostsPatchCommand = ReactiveCommand.Create(() =>
        {
            var path = HostsEditor.StagePatch(HostsMappings);
            HostsPatchInfo = HostsEditor.FormatInstructions(path);
            this.RaisePropertyChanged(nameof(HostsPatchInfo));
        });

        // Initial population.
        RefreshInstancesCommand?.Execute().Subscribe(_ => { }, _ => { });
        ApplyAutoFpsIfEnabled();
    }

    private string GameDirOfActiveInstance()
    {
        if (ActiveInstance is null) return Paths.GameDir;
        return InstanceManager.GameDirOf(ActiveInstance);
    }

    private void ApplyThemeFromSettings()
    {
        var mode = _settings.ThemeMode switch
        {
            1 => ThemeService.Mode.Light,
            2 => ThemeService.Mode.System,
            _ => ThemeService.Mode.Dark,
        };
        ThemeService.Apply(mode, _settings.AccentColor);
    }

    private void ApplyCompactModeFromSettings()
    {
        if (_owner is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_settings.CompactMode) { _owner.Width = 520; _owner.Height = 380; }
                else { _owner.Width = 1100; _owner.Height = 720; }
            }
            catch (Exception ex) { AppLogger.Warn("ApplyCompactMode: " + ex.Message); }
        });
    }

    private void ApplyAutoFpsIfEnabled()
    {
        if (!_settings.AutoFpsProfile) return;
        try
        {
            var gpu = Diagnostics.DetectGpu();
            var prof = Diagnostics.RecommendProfile((int)SystemInfo.TotalRamMb, gpu);
            if (prof != _settings.FpsProfile) FpsProfileIndex = prof;
        }
        catch (Exception ex) { AppLogger.Warn("ApplyAutoFps: " + ex.Message); }
    }
}
