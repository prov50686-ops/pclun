using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace PcLun.Services;

public record BackupInfo(string FileName, string FullPath, DateTime Created, long SizeBytes);

/// <summary>
/// Бэкапы папки `saves/` в zip и обратно. Хранятся в `<root>/backups/saves`.
/// </summary>
public static class BackupService
{
    public static string SavesDir => Path.Combine(Paths.GameDir, "saves");
    public static string BackupRoot => Path.Combine(Paths.LauncherRoot, "backups", "saves");

    public static List<BackupInfo> List()
    {
        Directory.CreateDirectory(BackupRoot);
        return new DirectoryInfo(BackupRoot)
            .GetFiles("*.zip")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new BackupInfo(f.Name, f.FullName, f.LastWriteTime, f.Length))
            .ToList();
    }

    public static async Task<BackupInfo> CreateAsync(string? label = null)
    {
        Directory.CreateDirectory(BackupRoot);
        if (!Directory.Exists(SavesDir))
            throw new DirectoryNotFoundException("Папка saves/ не найдена. Сыграйте хотя бы один раз.");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "backup" : Sanitize(label);
        var dest = Path.Combine(BackupRoot, $"{safeLabel}-{stamp}.zip");

        await Task.Run(() =>
        {
            // ZipFile.CreateFromDirectory не поддерживает overwrite в .NET 8 без перегрузки.
            if (File.Exists(dest)) File.Delete(dest);
            ZipFile.CreateFromDirectory(SavesDir, dest, CompressionLevel.Fastest, includeBaseDirectory: false);
        }).ConfigureAwait(false);

        var fi = new FileInfo(dest);
        AppLogger.Info($"Backup created: {dest} ({fi.Length / 1024} KB)");
        return new BackupInfo(fi.Name, fi.FullName, fi.LastWriteTime, fi.Length);
    }

    public static async Task RestoreAsync(BackupInfo backup, bool replaceExisting)
    {
        if (!File.Exists(backup.FullPath))
            throw new FileNotFoundException("Бэкап не найден: " + backup.FullPath);
        Directory.CreateDirectory(SavesDir);

        if (replaceExisting && Directory.Exists(SavesDir))
        {
            // Перед перезаписью кладём текущее в *.before-restore.zip — на всякий случай.
            try
            {
                await CreateAsync("before-restore").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Pre-restore backup failed: " + ex.Message);
            }
            foreach (var sub in Directory.GetDirectories(SavesDir))
                Directory.Delete(sub, recursive: true);
            foreach (var file in Directory.GetFiles(SavesDir))
                File.Delete(file);
        }

        await Task.Run(() => ZipFile.ExtractToDirectory(backup.FullPath, SavesDir, overwriteFiles: true))
            .ConfigureAwait(false);
        AppLogger.Info($"Backup restored from {backup.FullPath}");
    }

    public static void Delete(BackupInfo backup)
    {
        if (File.Exists(backup.FullPath)) File.Delete(backup.FullPath);
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(s.Where(c => !bad.Contains(c) && c != ' ').ToArray());
        return string.IsNullOrEmpty(clean) ? "backup" : clean[..Math.Min(clean.Length, 32)];
    }
}
