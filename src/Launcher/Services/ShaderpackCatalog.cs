using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

public record ShaderpackInfo(string Name, string Description, string Url, string FileName, string Tier);

/// <summary>
/// Каталог известных шейдерпаков для слабых ПК. Прямые ссылки взяты с официальных сайтов авторов.
/// </summary>
public static class ShaderpackCatalog
{
    public static IReadOnlyList<ShaderpackInfo> Items { get; } = new List<ShaderpackInfo>
    {
        new ShaderpackInfo(
            Name: "Sildur's Vibrant Lite v1.32",
            Description: "Один из самых лёгких популярных шейдеров. ~+10–20% FPS просадки на интегрированной графике.",
            Url: "https://sildurs-shaders.github.io/downloads/Sildurs+Vibrant+Shaders+v1.32+Lite.zip",
            FileName: "Sildurs+Vibrant+Shaders+v1.32+Lite.zip",
            Tier: "low"),
        new ShaderpackInfo(
            Name: "Builder's QoL",
            Description: "Минимальные тени и красивая вода без больших затрат FPS.",
            Url: "https://www.curseforge.com/api/v1/mods/463137/files/4416003/download",
            FileName: "BuildersQOL.zip",
            Tier: "low"),
        new ShaderpackInfo(
            Name: "Complementary Reimagined Lite",
            Description: "Сбалансированные шейдеры — тени, вода, лёгкие отражения.",
            Url: "https://www.curseforge.com/api/v1/mods/471206/files/4815320/download",
            FileName: "ComplementaryReimagined-Lite.zip",
            Tier: "mid"),
    };

    public static async Task<string> DownloadAsync(ShaderpackInfo pack, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var folder = ContentManager.FolderFor(ContentKind.Shaderpacks);
        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, pack.FileName);
        progress?.Report(new DownloadProgress($"Скачиваем шейдер: {pack.Name}", 0, 0));
        await Http.DownloadFileAsync(pack.Url, dest, ct: ct).ConfigureAwait(false);
        progress?.Report(new DownloadProgress($"Шейдер установлен: {pack.Name}", 1, 1));
        return dest;
    }
}
