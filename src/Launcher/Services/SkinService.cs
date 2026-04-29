using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Превью скина. Использует публичный mc-heads.net (генерирует 3D рендер по UUID).
/// Если у игрока офлайн-аккаунт — UUID детерминирован от ника, поэтому скин будет
/// показан, только если ник существует на mojang. Иначе показывается дефолтный (Steve/Alex).
/// </summary>
public static class SkinService
{
    /// <summary>URL изображения 3D-аватара. На загрузку отдельные запросы не нужны — это просто IMG src.</summary>
    public static string AvatarUrl(string nickOrUuid, int size = 200)
    {
        var clean = string.IsNullOrWhiteSpace(nickOrUuid) ? "Steve" : nickOrUuid.Trim();
        // mc-heads.net умеет принимать и ник, и UUID.
        return $"https://mc-heads.net/avatar/{Uri.EscapeDataString(clean)}/{size}";
    }

    public static string FullBodyUrl(string nickOrUuid, int height = 320)
    {
        var clean = string.IsNullOrWhiteSpace(nickOrUuid) ? "Steve" : nickOrUuid.Trim();
        return $"https://mc-heads.net/body/{Uri.EscapeDataString(clean)}/{height}";
    }

    /// <summary>Скачать аватар в файл (для оффлайн-кэша).</summary>
    public static async Task<string?> DownloadAvatarAsync(string nick, CancellationToken ct = default)
    {
        try
        {
            var url = AvatarUrl(nick);
            var dir = Path.Combine(Paths.LauncherRoot, "cache", "skins");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Sanitize(nick) + ".png");
            await Http.DownloadFileAsync(url, dest, ct: ct).ConfigureAwait(false);
            return dest;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SkinService download failed: " + ex.Message);
            return null;
        }
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new char[s.Length];
        for (int i = 0; i < s.Length; i++) clean[i] = Array.IndexOf(bad, s[i]) >= 0 ? '_' : s[i];
        return new string(clean);
    }
}
