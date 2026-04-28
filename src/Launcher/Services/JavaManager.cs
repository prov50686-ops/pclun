using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Авто-скачка Adoptium Temurin JRE 8u (нужен для Minecraft 1.16.5).
/// </summary>
public static class JavaManager
{
    // Adoptium API: latest GA build, JRE, hotspot, normal heap
    // Платформы: windows, linux, mac
    public static string DownloadUrlForCurrentOs()
    {
        string os = OperatingSystem.IsWindows() ? "windows"
                  : OperatingSystem.IsMacOS() ? "mac"
                  : "linux";
        string ext = OperatingSystem.IsWindows() ? "zip" : "tar.gz";
        // ext implicit in API response; we'll detect by content-type
        return $"https://api.adoptium.net/v3/binary/latest/8/ga/{os}/x64/jre/hotspot/normal/eclipse";
    }

    public static async Task<string> EnsureJavaAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        // 1) Если уже распакована — используем
        if (File.Exists(Paths.JavaExe) && IsJavaUsable(Paths.JavaExe))
            return Paths.JavaExe;

        // 2) Если в системе есть подходящая Java 8 — пробуем её
        var sysJava = TryFindSystemJava();
        if (sysJava is not null && IsJavaUsable(sysJava))
        {
            AppLogger.Info($"Using system Java: {sysJava}");
            return sysJava;
        }

        // 3) Скачиваем Adoptium Temurin
        Directory.CreateDirectory(Paths.JavaDir);
        progress?.Report(new DownloadProgress("Скачиваем Java 8 (Adoptium Temurin)…", 0, 0));

        var archivePath = Path.Combine(Paths.TempDir, OperatingSystem.IsWindows() ? "jre8.zip" : "jre8.tar.gz");
        await Http.DownloadFileAsync(DownloadUrlForCurrentOs(), archivePath, ct: ct).ConfigureAwait(false);

        progress?.Report(new DownloadProgress("Распаковка Java…", 0, 0));
        if (OperatingSystem.IsWindows())
        {
            ZipFile.ExtractToDirectory(archivePath, Paths.JavaDir, overwriteFiles: true);
        }
        else
        {
            // tar -xzf via process
            var psi = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{Paths.JavaDir}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0)
                throw new InvalidOperationException("Не удалось распаковать Java: " + await p.StandardError.ReadToEndAsync().ConfigureAwait(false));
        }

        try { File.Delete(archivePath); } catch { }

        if (!File.Exists(Paths.JavaExe))
            throw new FileNotFoundException("Java exe not found after extraction: " + Paths.JavaExe);

        // chmod +x for unix
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(Paths.JavaExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); } catch { }
        }

        return Paths.JavaExe;
    }

    public static bool IsJavaUsable(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo(javaPath, "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            return output.Contains("1.8.") || output.Contains("\"8.") || output.Contains("version \"8");
        }
        catch
        {
            return false;
        }
    }

    public static string? TryFindSystemJava()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("JAVA_HOME") is { } jh ? Path.Combine(jh, "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java") : null,
            "javaw",
            "java"
        };
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            try
            {
                var psi = new ProcessStartInfo(c, "-version") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p is null) continue;
                p.WaitForExit(3000);
                var err = p.StandardError.ReadToEnd();
                if (err.Contains("1.8.") || err.Contains("version \"8")) return c;
            }
            catch { }
        }
        return null;
    }
}
