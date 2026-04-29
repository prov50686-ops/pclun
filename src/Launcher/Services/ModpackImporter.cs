using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Modpack import: accepts either a local .zip or a remote URL.
/// Recognises Modrinth `.mrpack` (manifests as JSON in `modrinth.index.json`) and
/// generic `.zip` containing a `mods/` folder. CurseForge `.zip` (manifest.json)
/// is partially supported (extract overrides, leave manifest for future resolver).
/// </summary>
public static class ModpackImporter
{
    public class Result
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? InstanceName { get; set; }
        public int ModsExtracted { get; set; }
    }

    public static async Task<Result> ImportAsync(string source, string instanceName)
    {
        try
        {
            string zipPath;
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                zipPath = Path.Combine(Paths.TempDir, "modpack-" + DateTime.Now.Ticks + ".zip");
                using var resp = await Http.Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return new Result { Error = $"HTTP {(int)resp.StatusCode}" };
                using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs);
            }
            else
            {
                if (!File.Exists(source)) return new Result { Error = "file not found" };
                zipPath = source;
            }

            var instance = InstanceManager.Create(instanceName, "1.16.5", "forge");
            var gameDir = InstanceManager.GameDirOf(instance);

            using var archive = ZipFile.OpenRead(zipPath);
            int mods = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var name = entry.FullName.Replace('\\', '/');
                string? targetRel = name switch
                {
                    var n when n.StartsWith("overrides/") => n.Substring("overrides/".Length),
                    var n when n.StartsWith("mods/") => n,
                    var n when n.StartsWith("config/") => n,
                    var n when n.StartsWith("resourcepacks/") => n,
                    var n when n.StartsWith("shaderpacks/") => n,
                    _ => null,
                };
                if (targetRel is null) continue;
                var target = Path.Combine(gameDir, targetRel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                if (target.Contains("/mods/") || target.Contains("\\mods\\")) mods++;
            }
            return new Result { Ok = true, InstanceName = instance.Name, ModsExtracted = mods };
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ModpackImporter.ImportAsync failed: " + ex.Message);
            return new Result { Error = ex.Message };
        }
    }
}
