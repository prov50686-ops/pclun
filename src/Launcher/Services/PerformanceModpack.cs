using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

public record ModInfo(string Name, string Description, string Url, string FileName);

/// <summary>
/// Установка пакета производительных модов для 1.16.5. Эти моды дают
/// заметный прирост FPS даже поверх OptiFine. Все они работают на Forge 1.16.5.
///
/// Forge 1.16.5 (36.2.42) — рекомендуемая версия для этих модов.
/// Скачивание Forge — не headless (это отдельный installer.jar). Лаунчер
/// просто кладёт jar в TempDir и просит пользователя один раз нажать «Установить».
/// </summary>
public static class PerformanceModpack
{
    public const string ForgeInstallerUrl =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/1.16.5-36.2.42/forge-1.16.5-36.2.42-installer.jar";
    public const string ForgeInstallerFileName = "forge-1.16.5-36.2.42-installer.jar";

    /// <summary>
    /// Курируемый список модов — каждый ссылается на CurseForge CDN, который
    /// не требует API-ключа. Версии зафиксированы под 1.16.5 / Forge.
    /// </summary>
    public static IReadOnlyList<ModInfo> Mods { get; } = new List<ModInfo>
    {
        new ModInfo("FerriteCore", "Снижает RAM-потребление чанков (~−30% RAM).",
            "https://mediafilez.forgecdn.net/files/3414/796/ferritecore-2.1.1-forge.jar",
            "ferritecore-2.1.1-forge.jar"),
        new ModInfo("Krypton (Forge port)", "Оптимизирует сетевой стек.",
            "https://mediafilez.forgecdn.net/files/3392/849/krypton-0.2.1.jar",
            "krypton-0.2.1.jar"),
        new ModInfo("Starlight (Forge)", "Заменяет vanilla light engine — большой прирост FPS при движении.",
            "https://mediafilez.forgecdn.net/files/3727/516/starlight-1.0.1+forge.jar",
            "starlight-1.0.1+forge.jar"),
        new ModInfo("Smooth Boot (Forge)", "Ускоряет загрузку и снижает спайки на старте.",
            "https://mediafilez.forgecdn.net/files/3306/452/smoothboot-forge-1.16.5-1.7.1.jar",
            "smoothboot-forge-1.16.5-1.7.1.jar"),
        new ModInfo("Entity Culling", "Не рендерит мобов, которых не видно — большой прирост FPS на серверах.",
            "https://mediafilez.forgecdn.net/files/3656/957/entityculling-forge-1.5.2-mc1.16.5.jar",
            "entityculling-forge-1.5.2-mc1.16.5.jar"),
        new ModInfo("Memory Leak Fix", "Чинит несколько утечек памяти в vanilla.",
            "https://mediafilez.forgecdn.net/files/3656/991/memoryleakfix-forge-1.16.5-1.0.0.jar",
            "memoryleakfix-forge-1.16.5-1.0.0.jar"),
    };

    public static async Task<string> EnsureForgeInstallerAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Paths.TempDir, ForgeInstallerFileName);
        if (!File.Exists(dest))
        {
            progress?.Report(new DownloadProgress("Скачиваем Forge installer…", 0, 0));
            await Http.DownloadFileAsync(ForgeInstallerUrl, dest, ct: ct).ConfigureAwait(false);
        }
        return dest;
    }

    public static async Task DownloadModsAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var folder = ContentManager.FolderFor(ContentKind.Mods);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < Mods.Count; i++)
        {
            var m = Mods[i];
            progress?.Report(new DownloadProgress($"[{i + 1}/{Mods.Count}] {m.Name}…", i, Mods.Count));
            var dest = Path.Combine(folder, m.FileName);
            try
            {
                await Http.DownloadFileAsync(m.Url, dest, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Mod {m.Name} download failed: {ex.Message}");
            }
        }
        progress?.Report(new DownloadProgress("Performance pack установлен.", Mods.Count, Mods.Count));
    }
}
