using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcLun.Services;

/// <summary>
/// Hardware + environment diagnostics for FPS auto-tuning, throttle protection,
/// GPU detection, network hints, and process priority management.
/// </summary>
public static class Diagnostics
{
    // ---------- GPU detection ----------

    public class GpuInfo
    {
        public string Name { get; set; } = "Unknown";
        public string Vendor { get; set; } = "";
        public string Warning { get; set; } = "";
    }

    public static GpuInfo DetectGpu()
    {
        var info = new GpuInfo();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("wmic", "path win32_VideoController get name")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is not null)
                {
                    var text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    var line = text.Split('\n').Skip(1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
                    if (!string.IsNullOrEmpty(line)) info.Name = line;
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                if (File.Exists("/usr/bin/lspci") || File.Exists("/sbin/lspci"))
                {
                    var psi = new ProcessStartInfo("lspci", "")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    if (p is not null)
                    {
                        var text = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(3000);
                        var line = text.Split('\n').FirstOrDefault(l => l.Contains("VGA", StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(line))
                        {
                            var idx = line.IndexOf(':');
                            info.Name = idx > 0 ? line[(idx + 1)..].Trim() : line.Trim();
                        }
                    }
                }
            }

            var lower = info.Name.ToLowerInvariant();
            if (lower.Contains("intel")) { info.Vendor = "Intel"; info.Warning = "Слабая встройка — выкл. блики, шейдеры; рендер 2-4."; }
            else if (lower.Contains("amd") || lower.Contains("radeon")) info.Vendor = "AMD";
            else if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("rtx") || lower.Contains("gtx")) info.Vendor = "NVIDIA";
            else if (lower.Contains("apple") || lower.Contains("m1") || lower.Contains("m2") || lower.Contains("m3")) info.Vendor = "Apple";
            if (lower.Contains("hd graphics") && (lower.Contains("3000") || lower.Contains("4000") || lower.Contains("2000")))
                info.Warning = "Очень слабая встройка. Используй профиль Potato и render distance 2.";
        }
        catch (Exception ex) { AppLogger.Warn("Diagnostics.DetectGpu: " + ex.Message); }
        return info;
    }

    // ---------- Auto-FPS profile from hardware ----------

    public static int RecommendProfile(int totalRamMb, GpuInfo gpu)
    {
        // Profile codes match LauncherSettings.FpsProfile: 0=Potato, 1=Ultra, 2=Balanced, 3=Quality
        var lower = gpu.Name.ToLowerInvariant();
        bool weakIntel = lower.Contains("hd graphics") || lower.Contains("uhd graphics");
        bool ram4 = totalRamMb < 6000;
        bool ram8 = totalRamMb < 12000;
        if (weakIntel || ram4) return 0; // Potato
        if (ram8) return 1; // Ultra FPS
        if (lower.Contains("rtx") || lower.Contains("rx 6") || lower.Contains("rx 7") || lower.Contains("apple")) return 3; // Quality
        return 2; // Balanced
    }

    // ---------- Network tuner ----------

    public static IReadOnlyList<string> NetworkJvmFlags() => new[]
    {
        "-Djava.net.preferIPv4Stack=true",
        "-Dsun.net.client.defaultConnectTimeout=15000",
        "-Dsun.net.client.defaultReadTimeout=30000",
        "-Dhttp.keepAlive=true",
        "-Dhttp.maxConnections=100",
    };

    // ---------- Thermal throttle (best-effort linux/macos) ----------

    public static double? ReadCpuTempCelsius()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var path in new[]
                {
                    "/sys/class/thermal/thermal_zone0/temp",
                    "/sys/class/thermal/thermal_zone1/temp",
                })
                {
                    if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var milli))
                        return milli / 1000.0;
                }
            }
        }
        catch (Exception ex) { AppLogger.Warn("Diagnostics.ReadCpuTemp: " + ex.Message); }
        return null;
    }

    // ---------- Process priority manager ----------

    [SupportedOSPlatform("windows")]
    public static void DemoteBackgroundOnWindows(string[] processNames)
    {
        try
        {
            foreach (var name in processNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.PriorityClass = ProcessPriorityClass.Idle; }
                    catch { /* access denied — ignore */ }
                }
            }
        }
        catch (Exception ex) { AppLogger.Warn("Diagnostics.DemoteBackground: " + ex.Message); }
    }

    public static void DemoteBackground()
    {
        if (!OperatingSystem.IsWindows()) return;
        DemoteBackgroundOnWindows(new[] { "chrome", "msedge", "Discord", "Spotify", "Slack", "Code", "firefox" });
    }
}
