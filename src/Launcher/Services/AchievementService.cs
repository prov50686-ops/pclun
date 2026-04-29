using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace PcLun.Services;

/// <summary>
/// Achievement tracker. Local SQLite-equivalent in JSON file, synced with backend.
/// </summary>
public static class AchievementService
{
    public class Achievement
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "🏆";
        public double Unlocked { get; set; }
    }

    private class ListResp
    {
        public List<Achievement> Items { get; set; } = new();
        public Dictionary<string, JsonElement> Catalog { get; set; } = new();
    }

    private static string Base => OnlineService.BackendUrl;
    private static string LocalFile => System.IO.Path.Combine(Paths.LauncherRoot, "achievements.json");

    public static async Task<IReadOnlyList<Achievement>> ListAsync(string nick)
    {
        try
        {
            var resp = await Http.Client.GetFromJsonAsync<ListResp>(
                $"{Base}/achievements/{HttpUtility.UrlEncode(nick)}");
            return resp?.Items ?? new List<Achievement>();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AchievementService.ListAsync failed: " + ex.Message);
            return LoadLocal();
        }
    }

    public static async Task<bool> UnlockAsync(string player, string code)
    {
        // Always record locally; remote is best-effort.
        SaveLocal(new Achievement { Code = code, Title = code, Unlocked = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        try
        {
            using var resp = await Http.Client.PostAsJsonAsync($"{Base}/achievements/unlock",
                new { player, code });
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AchievementService.UnlockAsync failed: " + ex.Message);
            return false;
        }
    }

    public static IReadOnlyList<Achievement> LoadLocal()
    {
        try
        {
            if (!System.IO.File.Exists(LocalFile)) return Array.Empty<Achievement>();
            return JsonSerializer.Deserialize<List<Achievement>>(System.IO.File.ReadAllText(LocalFile))
                ?? new List<Achievement>();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AchievementService.LoadLocal failed: " + ex.Message);
            return Array.Empty<Achievement>();
        }
    }

    private static void SaveLocal(Achievement a)
    {
        try
        {
            var list = LoadLocal().ToList();
            if (list.Find(x => x.Code == a.Code) is null)
            {
                list.Add(a);
                System.IO.File.WriteAllText(LocalFile,
                    JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception ex) { AppLogger.Warn("AchievementService.SaveLocal: " + ex.Message); }
    }
}
