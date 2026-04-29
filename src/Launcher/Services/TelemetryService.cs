using System;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Anonymous telemetry: crashes, FPS averages, GPU detect. Off by default,
/// user opts in via LauncherSettings.TelemetryEnabled.
/// </summary>
public static class TelemetryService
{
    public static async Task SendAsync(string kind, object payload, bool enabled)
    {
        if (!enabled) return;
        try
        {
            using var resp = await Http.Client.PostAsJsonAsync(
                $"{OnlineService.BackendUrl}/telemetry",
                new { kind, payload });
            _ = resp; // ignore status
        }
        catch (Exception ex) { AppLogger.Warn("Telemetry failed: " + ex.Message); }
    }

    public static Task ReportCrashAsync(string diagnosis, bool enabled) =>
        SendAsync("crash", new { diagnosis, version = "0.5.0", os = Environment.OSVersion.ToString() }, enabled);

    public static Task ReportPerfAsync(double avgFps, int ramMb, string profile, bool enabled) =>
        SendAsync("perf", new { avgFps, ramMb, profile }, enabled);

    public static Task ReportGpuAsync(string gpuName, bool enabled) =>
        SendAsync("gpu", new { gpu = gpuName }, enabled);
}
