using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Downloads new launcher binary into a staging folder while the user plays.
/// On exit, the launcher binary can be swapped (per-OS) by the OS or by a
/// helper script. Here we just download — the swap is intentionally manual to
/// avoid breaking the running process.
/// </summary>
public static class BackgroundUpdater
{
    public static string StagingDir => Path.Combine(Paths.LauncherRoot, "update-staging");

    public static async Task<bool> StagePendingAsync(string downloadUrl, string filename, CancellationToken token = default)
    {
        try
        {
            Directory.CreateDirectory(StagingDir);
            var target = Path.Combine(StagingDir, filename);
            // Skip if already staged.
            if (File.Exists(target) && new FileInfo(target).Length > 0) return true;
            using var resp = await Http.Client.GetAsync(downloadUrl,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
            if (!resp.IsSuccessStatusCode) return false;
            using var fs = File.Create(target);
            await resp.Content.CopyToAsync(fs, token);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BackgroundUpdater.StagePending failed: " + ex.Message);
            return false;
        }
    }

    public static bool HasPending()
    {
        try
        {
            return Directory.Exists(StagingDir) &&
                   Directory.GetFiles(StagingDir).Length > 0;
        }
        catch { return false; }
    }
}
