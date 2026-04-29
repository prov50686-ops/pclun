using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Проверяет, есть ли свежий релиз на GitHub. Сравнивает текущую сборку с
/// `tag_name` последнего релиза. Не качает обновление автоматически — просто
/// возвращает URL/версию, чтобы UI мог показать кнопку «обновить».
/// </summary>
public static class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/prov50686-ops/pclun/releases/latest";

    public record Release(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("published_at")] string? PublishedAt);

    public record UpdateInfo(string CurrentVersion, string LatestVersion, string Url, string? Notes, bool IsNewer);

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, ReleasesApi);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.Client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var rel = JsonSerializer.Deserialize<Release>(text);
            if (rel is null || string.IsNullOrWhiteSpace(rel.TagName)) return null;

            var current = CurrentVersion;
            var latest = rel.TagName.TrimStart('v', 'V');
            return new UpdateInfo(current, latest, rel.HtmlUrl, rel.Body, IsNewer(latest, current));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateService check failed: " + ex.Message);
            return null;
        }
    }

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OpenInBrowser failed: " + ex.Message);
        }
    }

    /// <summary>True если `a` строго новее `b`. Сравнение по числовым сегментам.</summary>
    public static bool IsNewer(string a, string b)
    {
        var ap = ParseVersion(a);
        var bp = ParseVersion(b);
        for (int i = 0; i < Math.Max(ap.Length, bp.Length); i++)
        {
            int x = i < ap.Length ? ap[i] : 0;
            int y = i < bp.Length ? bp[i] : 0;
            if (x > y) return true;
            if (x < y) return false;
        }
        return false;
    }

    private static int[] ParseVersion(string v)
    {
        return v.Split('.', '-')
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();
    }
}
