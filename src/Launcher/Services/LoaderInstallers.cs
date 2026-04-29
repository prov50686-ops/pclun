using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// One-click installers for known mods/loaders that don't fit ContentManager.
/// All targets default to MC 1.16.5; URLs may need refresh over time.
/// </summary>
public static class LoaderInstallers
{
    public class InstallerInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Url { get; set; } = "";
        public string TargetSubdir { get; set; } = "mods"; // mods | shaderpacks | resourcepacks
        public string Filename { get; set; } = "";
        public string? RequiresLoader { get; set; } // e.g. "fabric"
    }

    public static IReadOnlyList<InstallerInfo> Catalog => new[]
    {
        new InstallerInfo
        {
            Name = "Replay Mod 1.16.5",
            Description = "Записывает игровые сессии и позволяет монтировать ролики из reply-файлов.",
            Url = "https://www.replaymod.com/download/replaymod-2.6.4-1.16.5.jar",
            Filename = "replaymod-2.6.4-1.16.5.jar",
            TargetSubdir = "mods",
        },
        new InstallerInfo
        {
            Name = "Sodium (Fabric)",
            Description = "Огромный буст FPS. Только Fabric, не Forge.",
            Url = "https://cdn.modrinth.com/data/AANobbMI/versions/0.2.0%2Bbuild.4/sodium-fabric-mc1.16.5-0.2.0%2Bbuild.4.jar",
            Filename = "sodium-fabric-1.16.5.jar",
            TargetSubdir = "mods",
            RequiresLoader = "fabric",
        },
        new InstallerInfo
        {
            Name = "Iris (Fabric, шейдеры)",
            Description = "Шейдеры на Fabric+Sodium. Альтернатива OptiFine.",
            Url = "https://cdn.modrinth.com/data/YL57xq9U/versions/1.1.2%2Bmc1.16.5/Iris-mc1.16.5-1.1.2.jar",
            Filename = "iris-fabric-1.16.5.jar",
            TargetSubdir = "mods",
            RequiresLoader = "fabric",
        },
        new InstallerInfo
        {
            Name = "Vivecraft (VR)",
            Description = "Поддержка VR-шлемов. Требует SteamVR/OpenXR.",
            Url = "https://github.com/jrbudda/Vivecraft_118/releases/download/mc-1.16.5/Vivecraft-1.16.5-jrbuddas-1.16.5.jar",
            Filename = "vivecraft-1.16.5.jar",
            TargetSubdir = "mods",
        },
        new InstallerInfo
        {
            Name = "Fabric Loader 0.15.x (1.16.5)",
            Description = "Лоадер модов Fabric. Альтернатива Forge — легче, быстрее.",
            Url = "https://maven.fabricmc.net/net/fabricmc/fabric-installer/0.11.2/fabric-installer-0.11.2.jar",
            Filename = "fabric-installer.jar",
            TargetSubdir = "", // ставится в корень папки игры
        },
    };

    public static async Task<bool> InstallAsync(InstallerInfo info, string gameDir)
    {
        try
        {
            var subdir = string.IsNullOrEmpty(info.TargetSubdir) ? gameDir : Path.Combine(gameDir, info.TargetSubdir);
            Directory.CreateDirectory(subdir);
            var bytes = await Http.Client.GetByteArrayAsync(info.Url);
            File.WriteAllBytes(Path.Combine(subdir, info.Filename), bytes);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"LoaderInstallers.{info.Name} failed: {ex.Message}");
            return false;
        }
    }
}
