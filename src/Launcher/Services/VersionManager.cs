using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Resolves available MC versions from Mojang piston-meta.
/// Falls back to a curated short list if the API is unreachable.
/// </summary>
public static class VersionManager
{
    private const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly string[] FallbackPopular = { "1.20.1", "1.19.4", "1.18.2", "1.16.5", "1.12.2", "1.8.9" };

    public class VersionInfo
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "release";
    }

    public static async Task<IReadOnlyList<string>> ListReleasesAsync(int max = 50)
    {
        try
        {
            using var resp = await Http.Client.GetAsync(Manifest);
            if (!resp.IsSuccessStatusCode) return FallbackPopular;
            var doc = await resp.Content.ReadFromJsonAsync<MojangManifest>();
            if (doc?.Versions is null) return FallbackPopular;
            return doc.Versions
                .Where(v => v.Type == "release")
                .Select(v => v.Id)
                .Take(max)
                .ToArray();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("VersionManager.ListReleasesAsync failed: " + ex.Message);
            return FallbackPopular;
        }
    }

    public static IReadOnlyList<string> PopularPresets => new[]
    {
        "1.20.1",  // современные сборки
        "1.19.4",
        "1.18.2",  // для слабых ПК с современным контентом
        "1.16.5",  // основной + OptiFine
        "1.12.2",  // классика модов / RLCraft
        "1.8.9",   // PvP / анархия
    };

    private class MojangManifest
    {
        public List<VersionInfo> Versions { get; set; } = new();
    }
}
