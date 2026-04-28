using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Скачивает и устанавливает OptiFine 1.16.5 HD U G8 для уже установленной vanilla 1.16.5.
/// </summary>
public static class OptifineInstaller
{
    public const string OptifineFile = "OptiFine_1.16.5_HD_U_G8.jar";
    public const string OptifineVersionDir = "1.16.5-OptiFine_HD_U_G8";
    private const string AdloadxUrl = "https://optifine.net/adloadx?f=" + OptifineFile;
    private const string Referer = "https://optifine.net/adloadx?f=" + OptifineFile;

    public static async Task InstallAsync(string javaPath, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var optifineDir = Path.Combine(Paths.VersionsDir, OptifineVersionDir);
        var optifineJsonPath = Path.Combine(optifineDir, OptifineVersionDir + ".json");
        if (File.Exists(optifineJsonPath))
        {
            progress?.Report(new DownloadProgress("OptiFine уже установлен.", 1, 1));
            return;
        }

        progress?.Report(new DownloadProgress("Получаем OptiFine с optifine.net…", 0, 0));

        var jarPath = Path.Combine(Paths.TempDir, OptifineFile);

        // 1) Try direct downloadx URL — works for older releases
        var directUrl = $"https://optifine.net/downloadx?f={OptifineFile}&x=";
        // optifine.net требует токен из adloadx-страницы
        try
        {
            var html = await Http.GetStringAsync(AdloadxUrl, ct).ConfigureAwait(false);
            var m = Regex.Match(html, @"downloadx\?f=" + Regex.Escape(OptifineFile) + @"&amp;x=([a-f0-9]+)");
            if (m.Success)
            {
                var token = m.Groups[1].Value;
                var url = $"https://optifine.net/downloadx?f={OptifineFile}&x={token}";
                await Http.DownloadFileAsync(url, jarPath, ct: ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OptiFine adloadx scrape failed: " + ex.Message);
        }

        if (!File.Exists(jarPath) || new FileInfo(jarPath).Length < 100_000)
        {
            // Fallback to mirror (optifined.net is a known unofficial mirror)
            var mirror = $"https://optifined.net/file/optifine_1.16.5/{OptifineFile}";
            try
            {
                await Http.DownloadFileAsync(mirror, jarPath, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("OptiFine mirror download failed", ex);
                throw new InvalidOperationException(
                    "Не удалось скачать OptiFine. Положите " + OptifineFile + " вручную в папку " + Paths.TempDir);
            }
        }

        progress?.Report(new DownloadProgress("Запускаем установщик OptiFine…", 0, 0));

        // 2) Run installer in headless mode: java -cp OptiFine.jar optifine.Installer
        // Передаём через системное свойство путь к .minecraft, иначе попытается ставить в default.
        // Mojang launcher хранит minecraft в gameDir; OptiFine инсталлятор использует OPTIFINE_INSTALLER_TARGET
        // или системное свойство user.home/AppData. Чтобы поставить в наш gameDir, выставим user.home.
        var psi = new ProcessStartInfo(javaPath, $"-Duser.home=\"{Path.GetDirectoryName(Paths.GameDir)}\" -cp \"{jarPath}\" optifine.Installer")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = Paths.TempDir
        };
        // Note: OptiFine installer ожидает что .minecraft в %APPDATA% (Win) или ~/.minecraft.
        // Проще: положим симлинк/каталог .minecraft -> Paths.GameDir и выставим user.home соответственно.
        // На Windows %APPDATA% -> %USERPROFILE%/AppData/Roaming, поэтому делаем:
        // <fakeHome>/AppData/Roaming/.minecraft -> Paths.GameDir
        var fakeHome = SetupFakeHome();
        psi.EnvironmentVariables["APPDATA"] = Path.Combine(fakeHome, "AppData", "Roaming");
        psi.EnvironmentVariables["USERPROFILE"] = fakeHome;
        psi.Arguments = $"-Duser.home=\"{fakeHome}\" -cp \"{jarPath}\" optifine.Installer";

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        AppLogger.Info("OptiFine installer stdout: " + stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) AppLogger.Warn("OptiFine installer stderr: " + stderr);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Установщик OptiFine завершился с кодом {p.ExitCode}: {stderr}");

        if (!File.Exists(optifineJsonPath))
            throw new FileNotFoundException("OptiFine JSON не появился после установки", optifineJsonPath);

        progress?.Report(new DownloadProgress("OptiFine установлен.", 1, 1));
    }

    private static string SetupFakeHome()
    {
        var fakeHome = Path.Combine(Paths.LauncherRoot, "fake_home");
        Directory.CreateDirectory(fakeHome);
        var minecraftLink = OperatingSystem.IsWindows()
            ? Path.Combine(fakeHome, "AppData", "Roaming", ".minecraft")
            : Path.Combine(fakeHome, ".minecraft");

        Directory.CreateDirectory(Path.GetDirectoryName(minecraftLink)!);

        if (!Directory.Exists(minecraftLink))
        {
            try
            {
                // На Windows нужны привилегии для symlink, поэтому используем junction если возможно
                Directory.CreateSymbolicLink(minecraftLink, Paths.GameDir);
            }
            catch
            {
                // fallback: junction via cmd /c mklink /J
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{minecraftLink}\" \"{Paths.GameDir}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                }
                else
                {
                    // unix: regular ln -s
                    var psi = new ProcessStartInfo("ln", $"-s \"{Paths.GameDir}\" \"{minecraftLink}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                }
            }
        }

        return fakeHome;
    }
}
