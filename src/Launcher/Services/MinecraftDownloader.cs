using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Скачивает версию Minecraft (json + client.jar + библиотеки + ассеты + нативы).
/// Работает с Mojang piston-meta API.
/// </summary>
public class MinecraftDownloader
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string AssetsBase = "https://resources.download.minecraft.net";

    public string TargetVersion { get; }

    public MinecraftDownloader(string version)
    {
        TargetVersion = version;
    }

    public record InstallResult(string VersionDir, string VersionJsonPath, string ClientJarPath, JsonDocument VersionJson);

    public async Task<InstallResult> InstallAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new DownloadProgress("Получаем манифест версий…", 0, 0));
        var manifestText = await Http.GetStringAsync(VersionManifestUrl, ct).ConfigureAwait(false);
        using var manifest = JsonDocument.Parse(manifestText);

        string? versionUrl = null;
        string? versionSha = null;
        foreach (var v in manifest.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (v.GetProperty("id").GetString() == TargetVersion)
            {
                versionUrl = v.GetProperty("url").GetString();
                if (v.TryGetProperty("sha1", out var sha)) versionSha = sha.GetString();
                break;
            }
        }
        if (versionUrl is null) throw new InvalidOperationException($"Версия {TargetVersion} не найдена в манифесте.");

        var versionDir = Path.Combine(Paths.VersionsDir, TargetVersion);
        Directory.CreateDirectory(versionDir);
        var versionJsonPath = Path.Combine(versionDir, $"{TargetVersion}.json");

        progress?.Report(new DownloadProgress($"Скачиваем описание {TargetVersion}…", 0, 0));
        await Http.DownloadFileAsync(versionUrl, versionJsonPath, versionSha, ct: ct).ConfigureAwait(false);
        var versionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(versionJsonPath, ct).ConfigureAwait(false));
        var root = versionDoc.RootElement;

        // ---- client jar ----
        var client = root.GetProperty("downloads").GetProperty("client");
        var clientUrl = client.GetProperty("url").GetString()!;
        var clientSha = client.GetProperty("sha1").GetString();
        var clientJar = Path.Combine(versionDir, $"{TargetVersion}.jar");
        progress?.Report(new DownloadProgress("Скачиваем client.jar…", 0, 0));
        await Http.DownloadFileAsync(clientUrl, clientJar, clientSha, ct: ct).ConfigureAwait(false);

        // ---- libraries ----
        var libraries = root.GetProperty("libraries").EnumerateArray().ToList();
        for (int i = 0; i < libraries.Count; i++)
        {
            var lib = libraries[i];
            if (!IsLibraryAllowed(lib)) continue;

            // artifact
            if (lib.TryGetProperty("downloads", out var dl))
            {
                if (dl.TryGetProperty("artifact", out var art))
                {
                    var path = art.GetProperty("path").GetString()!;
                    var url = art.GetProperty("url").GetString()!;
                    var sha = art.TryGetProperty("sha1", out var s) ? s.GetString() : null;
                    var dest = Path.Combine(Paths.LibrariesDir, path);
                    progress?.Report(new DownloadProgress($"Библиотеки {i + 1}/{libraries.Count}: {Path.GetFileName(path)}", i, libraries.Count));
                    await Http.DownloadFileAsync(url, dest, sha, ct: ct).ConfigureAwait(false);
                }

                // natives
                var nativeKey = NativeClassifierKey();
                if (nativeKey is not null && lib.TryGetProperty("natives", out var natives))
                {
                    if (natives.TryGetProperty(nativeKey, out var classifierProp))
                    {
                        var classifier = classifierProp.GetString();
                        if (classifier is not null && dl.TryGetProperty("classifiers", out var classifiers)
                            && classifiers.TryGetProperty(classifier, out var nativeArt))
                        {
                            var path = nativeArt.GetProperty("path").GetString()!;
                            var url = nativeArt.GetProperty("url").GetString()!;
                            var sha = nativeArt.TryGetProperty("sha1", out var ns) ? ns.GetString() : null;
                            var dest = Path.Combine(Paths.LibrariesDir, path);
                            await Http.DownloadFileAsync(url, dest, sha, ct: ct).ConfigureAwait(false);

                            // extract natives
                            ExtractNatives(dest, Paths.NativesDir, GetNativeExcludes(lib));
                        }
                    }
                }
            }
        }

        // ---- asset index ----
        var assetIndex = root.GetProperty("assetIndex");
        var indexUrl = assetIndex.GetProperty("url").GetString()!;
        var indexSha = assetIndex.GetProperty("sha1").GetString();
        var indexId = assetIndex.GetProperty("id").GetString()!;
        var indexPath = Path.Combine(Paths.AssetsIndexes, indexId + ".json");

        progress?.Report(new DownloadProgress("Скачиваем индекс ассетов…", 0, 0));
        await Http.DownloadFileAsync(indexUrl, indexPath, indexSha, ct: ct).ConfigureAwait(false);

        // ---- assets ----
        using var indexDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false));
        var objects = indexDoc.RootElement.GetProperty("objects");
        var totalAssets = objects.EnumerateObject().Count();
        int done = 0;
        foreach (var entry in objects.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            var hash = entry.Value.GetProperty("hash").GetString()!;
            var prefix = hash[..2];
            var dest = Path.Combine(Paths.AssetsObjects, prefix, hash);
            var url = $"{AssetsBase}/{prefix}/{hash}";
            await Http.DownloadFileAsync(url, dest, hash, ct: ct).ConfigureAwait(false);
            done++;
            if (done % 16 == 0 || done == totalAssets)
                progress?.Report(new DownloadProgress($"Ассеты {done}/{totalAssets}", done, totalAssets));
        }

        progress?.Report(new DownloadProgress("Установка завершена.", 1, 1));
        return new InstallResult(versionDir, versionJsonPath, clientJar, versionDoc);
    }

    public static bool IsLibraryAllowed(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules)) return true;

        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.GetProperty("action").GetString();
            bool applies = true;
            if (rule.TryGetProperty("os", out var os))
            {
                applies = false;
                var name = os.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null || OsMatches(name)) applies = true;
            }

            if (applies)
            {
                allowed = action == "allow";
            }
        }
        return allowed;
    }

    public static bool OsMatches(string name) => name switch
    {
        "windows" => OperatingSystem.IsWindows(),
        "linux" => OperatingSystem.IsLinux(),
        "osx" => OperatingSystem.IsMacOS(),
        _ => false
    };

    public static string? NativeClassifierKey()
    {
        if (OperatingSystem.IsWindows()) return "natives-windows";
        if (OperatingSystem.IsLinux()) return "natives-linux";
        if (OperatingSystem.IsMacOS()) return "natives-osx";
        return null;
    }

    private static List<string> GetNativeExcludes(JsonElement lib)
    {
        var list = new List<string>();
        if (lib.TryGetProperty("extract", out var ex) && ex.TryGetProperty("exclude", out var arr))
        {
            foreach (var v in arr.EnumerateArray())
                if (v.GetString() is { } s) list.Add(s);
        }
        return list;
    }

    private static void ExtractNatives(string archivePath, string destDir, List<string> excludes)
    {
        Directory.CreateDirectory(destDir);
        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // dir
            if (excludes.Any(e => entry.FullName.StartsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
            var dest = Path.Combine(destDir, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                entry.ExtractToFile(dest, overwrite: true);
            }
            catch (IOException)
            {
                // file in use, skip
            }
        }
    }
}

public record DownloadProgress(string Status, int Current, int Total)
{
    public double Percent => Total <= 0 ? 0 : Math.Clamp(Current * 100.0 / Total, 0, 100);
}
