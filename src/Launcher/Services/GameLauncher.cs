using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

public record LaunchOptions(
    string VanillaVersionId,
    string? OptifineVersionId,
    string Username,
    string Uuid,
    string AccessToken,
    string UserType,        // "msa" or "legacy"
    int RamMb,
    int WindowWidth,
    int WindowHeight,
    bool Fullscreen,
    bool UseOptimizedJvm,
    FpsProfile Profile);

public static class GameLauncher
{
    public static async Task<Process> LaunchAsync(string javaPath, LaunchOptions opt, CancellationToken ct = default)
    {
        // Загружаем основной version json (vanilla или optifine — он inheritsFrom vanilla)
        var primaryId = opt.OptifineVersionId ?? opt.VanillaVersionId;
        var primaryDir = Path.Combine(Paths.VersionsDir, primaryId);
        var primaryJsonPath = Path.Combine(primaryDir, primaryId + ".json");
        if (!File.Exists(primaryJsonPath))
            throw new FileNotFoundException("Не найден JSON версии: " + primaryJsonPath);

        using var primaryDoc = JsonDocument.Parse(await File.ReadAllTextAsync(primaryJsonPath, ct).ConfigureAwait(false));
        var merged = MergeWithParent(primaryDoc.RootElement);

        // assetIndex
        var assetIndexId = merged.AssetIndexId ?? opt.VanillaVersionId;

        // Собираем classpath
        var classpath = new List<string>();
        foreach (var lib in merged.Libraries)
        {
            if (!MinecraftDownloader.IsLibraryAllowed(lib.Element)) continue;
            if (lib.Element.TryGetProperty("downloads", out var dl)
                && dl.TryGetProperty("artifact", out var art)
                && art.TryGetProperty("path", out var pathEl))
            {
                var path = Path.Combine(Paths.LibrariesDir, pathEl.GetString()!);
                if (File.Exists(path) && !classpath.Contains(path))
                    classpath.Add(path);
            }
            else if (lib.Element.TryGetProperty("name", out var nm))
            {
                // OptiFine library без downloads — резолвим по maven-нотации.
                var path = ResolveMavenPath(nm.GetString()!);
                if (path is not null && File.Exists(Path.Combine(Paths.LibrariesDir, path)))
                {
                    var full = Path.Combine(Paths.LibrariesDir, path);
                    if (!classpath.Contains(full)) classpath.Add(full);
                }
            }
        }

        // client jar (vanilla)
        var clientJar = Path.Combine(Paths.VersionsDir, opt.VanillaVersionId, opt.VanillaVersionId + ".jar");
        if (File.Exists(clientJar)) classpath.Add(clientJar);

        // OptiFine jar (если есть свой jar в папке версии — добавим)
        if (opt.OptifineVersionId is not null)
        {
            var ofJar = Path.Combine(Paths.VersionsDir, opt.OptifineVersionId, opt.OptifineVersionId + ".jar");
            if (File.Exists(ofJar) && !classpath.Contains(ofJar)) classpath.Add(ofJar);
        }

        var sep = OperatingSystem.IsWindows() ? ";" : ":";
        var cpString = string.Join(sep, classpath);

        // JVM args
        var jvmArgs = new List<string>();
        if (opt.UseOptimizedJvm)
            jvmArgs.AddRange(OptimizationProfile.GetJvmArgs(opt.RamMb));
        else
        {
            jvmArgs.Add($"-Xms{opt.RamMb}M");
            jvmArgs.Add($"-Xmx{opt.RamMb}M");
        }

        jvmArgs.Add($"-Djava.library.path={Paths.NativesDir}");
        jvmArgs.Add($"-Dminecraft.launcher.brand=PcLun");
        jvmArgs.Add($"-Dminecraft.launcher.version=0.1.0");
        jvmArgs.Add("-cp");
        jvmArgs.Add(cpString);

        var mainClass = merged.MainClass ?? "net.minecraft.client.main.Main";

        // Game args
        var gameArgs = BuildGameArgs(merged.GameArgsTokens, opt, assetIndexId);

        // Финальная команда
        var finalArgs = new List<string>();
        finalArgs.AddRange(jvmArgs);
        finalArgs.Add(mainClass);
        finalArgs.AddRange(gameArgs);

        if (opt.Fullscreen)
            finalArgs.Add("--fullscreen");
        else
        {
            finalArgs.Add("--width");
            finalArgs.Add(opt.WindowWidth.ToString());
            finalArgs.Add("--height");
            finalArgs.Add(opt.WindowHeight.ToString());
        }

        // Запись опций перед стартом
        Directory.CreateDirectory(Paths.GameDir);
        OptimizationProfile.WriteOptionsTxt(Paths.GameDir, opt.Profile);
        OptimizationProfile.WriteOptifineTxt(Paths.GameDir, opt.Profile);

        var psi = new ProcessStartInfo(javaPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = Paths.GameDir
        };
        foreach (var a in finalArgs) psi.ArgumentList.Add(a);

        AppLogger.Info("Launching: " + javaPath + " " + string.Join(" ", finalArgs.Select(QuoteIfNeeded)));

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) AppLogger.Info("[mc] " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppLogger.Warn("[mc!] " + e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private record MergedManifest(string? MainClass, string? AssetIndexId, List<LibraryEntry> Libraries, List<JsonElement> GameArgsTokens);
    private record LibraryEntry(string Name, JsonElement Element);

    private static MergedManifest MergeWithParent(JsonElement root)
    {
        string? mainClass = root.TryGetProperty("mainClass", out var mc) ? mc.GetString() : null;
        string? assetIndexId = root.TryGetProperty("assetIndex", out var ai) && ai.TryGetProperty("id", out var aid) ? aid.GetString() : null;
        var libs = new Dictionary<string, LibraryEntry>(); // by name (overrides win)
        var gameArgs = new List<JsonElement>();

        // если есть inheritsFrom, читаем родителя
        if (root.TryGetProperty("inheritsFrom", out var parentIdEl) && parentIdEl.GetString() is { } parentId)
        {
            var parentJsonPath = Path.Combine(Paths.VersionsDir, parentId, parentId + ".json");
            if (File.Exists(parentJsonPath))
            {
                using var parentDoc = JsonDocument.Parse(File.ReadAllText(parentJsonPath));
                var parent = parentDoc.RootElement;
                if (mainClass is null && parent.TryGetProperty("mainClass", out var pmc)) mainClass = pmc.GetString();
                if (assetIndexId is null && parent.TryGetProperty("assetIndex", out var pai) && pai.TryGetProperty("id", out var paid)) assetIndexId = paid.GetString();
                AppendLibs(parent, libs);
                AppendGameArgs(parent, gameArgs);
            }
        }

        AppendLibs(root, libs);
        AppendGameArgs(root, gameArgs);

        return new MergedManifest(mainClass, assetIndexId, libs.Values.ToList(), gameArgs);
    }

    private static void AppendLibs(JsonElement v, Dictionary<string, LibraryEntry> libs)
    {
        if (!v.TryGetProperty("libraries", out var arr)) return;
        foreach (var lib in arr.EnumerateArray())
        {
            var name = lib.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : Guid.NewGuid().ToString();
            // OptiFine добавляет свои либы первыми; не позволяем родительским перезаписывать их.
            libs[name] = new LibraryEntry(name, lib.Clone());
        }
    }

    private static void AppendGameArgs(JsonElement v, List<JsonElement> gameArgs)
    {
        // Современный формат: arguments.game (массив строк или объектов с rules)
        if (v.TryGetProperty("arguments", out var args) && args.TryGetProperty("game", out var gameArr))
        {
            foreach (var t in gameArr.EnumerateArray()) gameArgs.Add(t.Clone());
            return;
        }
        // Старый формат: minecraftArguments — строка
        if (v.TryGetProperty("minecraftArguments", out var mca) && mca.GetString() is { } str)
        {
            foreach (var part in str.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                using var d = JsonDocument.Parse("\"" + part.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
                gameArgs.Add(d.RootElement.Clone());
            }
        }
    }

    private static List<string> BuildGameArgs(List<JsonElement> tokens, LaunchOptions opt, string assetIndexId)
    {
        var subs = new Dictionary<string, string>
        {
            ["auth_player_name"] = opt.Username,
            ["version_name"] = opt.OptifineVersionId ?? opt.VanillaVersionId,
            ["game_directory"] = Paths.GameDir,
            ["assets_root"] = Paths.AssetsDir,
            ["assets_index_name"] = assetIndexId,
            ["auth_uuid"] = opt.Uuid,
            ["auth_access_token"] = opt.AccessToken,
            ["clientid"] = "PcLun",
            ["auth_xuid"] = "0",
            ["user_type"] = opt.UserType,
            ["version_type"] = "release",
            ["user_properties"] = "{}",
            ["resolution_width"] = opt.WindowWidth.ToString(),
            ["resolution_height"] = opt.WindowHeight.ToString()
        };

        var result = new List<string>();
        foreach (var tok in tokens)
        {
            if (tok.ValueKind == JsonValueKind.String)
            {
                result.Add(Substitute(tok.GetString()!, subs));
            }
            else if (tok.ValueKind == JsonValueKind.Object)
            {
                // только rule "allow" для нашей ОС/feature пропускаем — для простоты пропускаем все условные (resolution, demo, custom_capes...)
                // и явно добавим resolution в LaunchAsync через --width/--height.
                continue;
            }
        }
        return result;
    }

    private static string Substitute(string template, Dictionary<string, string> subs)
    {
        var sb = new StringBuilder(template);
        foreach (var (k, v) in subs)
            sb.Replace("${" + k + "}", v);
        return sb.ToString();
    }

    public static string? ResolveMavenPath(string mavenName)
    {
        // Формат: groupId:artifactId:version[:classifier][@ext]
        // например: optifine:OptiFine:1.16.5_HD_U_G8
        try
        {
            var ext = "jar";
            var atIdx = mavenName.IndexOf('@');
            if (atIdx >= 0) { ext = mavenName[(atIdx + 1)..]; mavenName = mavenName[..atIdx]; }
            var parts = mavenName.Split(':');
            if (parts.Length < 3) return null;
            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? "-" + parts[3] : "";
            return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{ext}";
        }
        catch
        {
            return null;
        }
    }

    private static string QuoteIfNeeded(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
