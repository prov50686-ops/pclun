using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PcLun.Services;

/// <summary>
/// Простая локальная статистика игрока: суммарное время в игре, число запусков, последний сервер.
/// Хранится в `<root>/stats.json`. Обновляется при каждом старте игры (через `Begin()`/`End()`).
/// </summary>
public class PlayerStats
{
    [JsonPropertyName("total_seconds")] public long TotalSeconds { get; set; }
    [JsonPropertyName("sessions")] public int Sessions { get; set; }
    [JsonPropertyName("last_server")] public string LastServer { get; set; } = "";
    [JsonPropertyName("last_played")] public DateTime LastPlayed { get; set; }
    [JsonPropertyName("longest_session_seconds")] public long LongestSessionSeconds { get; set; }
    [JsonPropertyName("avg_fps")] public double? AvgFps { get; set; }

    public string Hours => TimeSpan.FromSeconds(TotalSeconds).TotalHours.ToString("0.0") + " ч";
    public string LastPlayedDisplay =>
        LastPlayed == default ? "—" : LastPlayed.ToString("yyyy-MM-dd HH:mm");
}

public static class PlayerStatsService
{
    private static string StatsFile => Path.Combine(Paths.LauncherRoot, "stats.json");

    public static PlayerStats Load()
    {
        try
        {
            if (!File.Exists(StatsFile)) return new PlayerStats();
            var text = File.ReadAllText(StatsFile);
            return JsonSerializer.Deserialize<PlayerStats>(text) ?? new PlayerStats();
        }
        catch
        {
            return new PlayerStats();
        }
    }

    public static void Save(PlayerStats stats)
    {
        try
        {
            File.WriteAllText(StatsFile, JsonSerializer.Serialize(stats,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PlayerStatsService.Save failed: " + ex.Message);
        }
    }

    public static void RecordSession(TimeSpan duration, string? lastServer)
    {
        var s = Load();
        s.TotalSeconds += (long)duration.TotalSeconds;
        s.Sessions += 1;
        s.LastPlayed = DateTime.Now;
        s.LongestSessionSeconds = Math.Max(s.LongestSessionSeconds, (long)duration.TotalSeconds);
        if (!string.IsNullOrWhiteSpace(lastServer)) s.LastServer = lastServer!;
        Save(s);
    }

    private static readonly Regex _fpsRegex = new(@"\bfps:\s*(\d+)\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Парсит средний FPS из последнего лога (если игра туда писал). Не критично, best-effort.
    /// </summary>
    public static double? AvgFpsFromLatestLog()
    {
        try
        {
            var log = Path.Combine(Paths.GameDir, "logs", "latest.log");
            if (!File.Exists(log)) return null;
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            int sum = 0, count = 0;
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                var m = _fpsRegex.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var fps))
                {
                    sum += fps;
                    count++;
                }
            }
            return count > 0 ? sum / (double)count : null;
        }
        catch
        {
            return null;
        }
    }

    public static List<string> RecentServersFromLog(int max = 5)
    {
        try
        {
            var log = Path.Combine(Paths.GameDir, "logs", "latest.log");
            if (!File.Exists(log)) return new List<string>();
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var rgx = new Regex(@"Connecting to ([^\s,]+),", RegexOptions.IgnoreCase);
            var seen = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                var m = rgx.Match(line);
                if (m.Success)
                {
                    var host = m.Groups[1].Value;
                    if (!seen.Contains(host)) seen.Add(host);
                }
            }
            seen.Reverse();
            return seen.Take(max).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
