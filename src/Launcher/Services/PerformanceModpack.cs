using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
///
/// Резолв ссылок идёт через Modrinth API: CurseForge mediafilez с 2022 года режет
/// прямые скачивания из third-party лаунчеров (403 Forbidden), а Modrinth даёт
/// открытый CDN для тех же модов.
/// </summary>
public static class PerformanceModpack
{
    public const string McVersion = "1.16.5";
    public const string Loader = "forge";

    public const string ForgeInstallerUrl =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/1.16.5-36.2.42/forge-1.16.5-36.2.42-installer.jar";
    public const string ForgeInstallerFileName = "forge-1.16.5-36.2.42-installer.jar";

    /// <summary>
    /// Курируемый список модов: Modrinth slug + локальное описание.
    /// Реальная ссылка на jar резолвится через Modrinth API на момент скачивания —
    /// поэтому ссылки никогда не «протухают», даже если автор перевыложил версию.
    /// Моды, у которых нет 1.16.5+forge на Modrinth, тут не лежат, чтобы не давать
    /// пользователю битый чекбокс.
    /// </summary>
    public static IReadOnlyList<ModInfo> Mods { get; } = new List<ModInfo>
    {
        new ModInfo("FerriteCore", "Снижает RAM-потребление чанков (~−30% RAM).",
            "modrinth:ferrite-core", "ferrite-core.jar"),
        new ModInfo("Entity Culling", "Не рендерит мобов, которых не видно — большой прирост FPS на серверах.",
            "modrinth:entityculling", "entityculling.jar"),
        new ModInfo("Memory Leak Fix", "Чинит несколько утечек памяти в vanilla.",
            "modrinth:memoryleakfix", "memoryleakfix.jar"),
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
            try
            {
                if (m.Url.StartsWith("modrinth:", StringComparison.Ordinal))
                {
                    var slug = m.Url.Substring("modrinth:".Length);
                    var versions = await ModrinthClient.ListVersionsAsync(slug, McVersion, Loader).ConfigureAwait(false);
                    var version = versions.FirstOrDefault();
                    var file = version?.Files.FirstOrDefault(f => f.Primary) ?? version?.Files.FirstOrDefault();
                    if (file is null || string.IsNullOrEmpty(file.Url))
                    {
                        AppLogger.Warn($"Mod {m.Name}: нет {Loader}-версии для MC {McVersion} на Modrinth, пропускаем.");
                        continue;
                    }
                    var dest = Path.Combine(folder, file.Filename);
                    await Http.DownloadFileAsync(file.Url, dest, ct: ct).ConfigureAwait(false);
                }
                else
                {
                    var dest = Path.Combine(folder, m.FileName);
                    await Http.DownloadFileAsync(m.Url, dest, ct: ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Mod {m.Name} download failed: {ex.Message}");
            }
        }
        progress?.Report(new DownloadProgress("Performance pack установлен.", Mods.Count, Mods.Count));
    }
}
