using System;
using System.IO;

namespace PcLun.Services;

/// <summary>
/// FPS overlay scaffolding. Real implementation requires a Forge mod that
/// hooks into the render pipeline and dumps avg FPS to launcher_logs/fps.txt.
/// We create the directory and read whatever the mod writes.
/// </summary>
public static class FpsOverlay
{
    public static string FpsLogPath => Path.Combine(Paths.LauncherRoot, "fps.log");

    public class FpsSnapshot
    {
        public double Avg { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public DateTime At { get; set; } = DateTime.Now;
        public bool HasData => Avg > 0;
    }

    public static FpsSnapshot ReadLatest()
    {
        var snap = new FpsSnapshot();
        try
        {
            if (!File.Exists(FpsLogPath)) return snap;
            // Format: "ts;avg;min;max" per line, latest at end.
            using var stream = new FileStream(FpsLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? last = null;
            while (reader.ReadLine() is { } line) last = line;
            if (string.IsNullOrWhiteSpace(last)) return snap;
            var parts = last.Split(';');
            if (parts.Length >= 4 &&
                double.TryParse(parts[1], out var avg) &&
                double.TryParse(parts[2], out var min) &&
                double.TryParse(parts[3], out var max))
            {
                snap.Avg = avg;
                snap.Min = min;
                snap.Max = max;
                if (long.TryParse(parts[0], out var ts))
                    snap.At = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FpsOverlay.ReadLatest failed: " + ex.Message);
        }
        return snap;
    }

    public static void RecordFromLog()
    {
        // Stub: parse latest.log for FPS lines emitted by F3 overlay if user has
        // OptiFine "Show FPS" enabled. This is a best-effort companion until
        // a Forge mod is shipped.
        try
        {
            var latest = Path.Combine(Paths.GameDir, "logs", "latest.log");
            if (!File.Exists(latest)) return;
            var lines = File.ReadAllLines(latest);
            int fpsSum = 0, count = 0, min = int.MaxValue, max = 0;
            foreach (var line in lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,4})\s*fps", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var fps) && fps < 1000)
                {
                    fpsSum += fps; count++;
                    if (fps < min) min = fps;
                    if (fps > max) max = fps;
                }
            }
            if (count == 0) return;
            var avg = fpsSum / (double)count;
            File.AppendAllText(FpsLogPath,
                $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()};{avg:F1};{min};{max}\n");
        }
        catch (Exception ex) { AppLogger.Warn("FpsOverlay.RecordFromLog: " + ex.Message); }
    }
}
