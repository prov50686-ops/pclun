using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Cloud backup via presigned URL from the backend. Backend returns
/// {ok, url, key} when configured; uploader does HTTP PUT.
/// </summary>
public static class CloudBackupService
{
    private class PresignResp { public bool Ok { get; set; } public string? Url { get; set; } public string? Error { get; set; } public string? Key { get; set; } }

    public static async Task<(bool ok, string message)> UploadAsync(string nick, string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return (false, "файл не найден");
            var filename = Path.GetFileName(filePath);
            using var resp = await Http.Client.PostAsJsonAsync($"{OnlineService.BackendUrl}/backup/presign",
                new { nick, filename });
            if (!resp.IsSuccessStatusCode) return (false, $"presign HTTP {(int)resp.StatusCode}");
            var data = await resp.Content.ReadFromJsonAsync<PresignResp>();
            if (data is null || !data.Ok || string.IsNullOrEmpty(data.Url))
                return (false, data?.Error ?? "облако не настроено на сервере");

            using var put = new HttpRequestMessage(HttpMethod.Put, data.Url);
            using var fs = File.OpenRead(filePath);
            put.Content = new StreamContent(fs);
            using var putResp = await Http.Client.SendAsync(put);
            return putResp.IsSuccessStatusCode
                ? (true, $"загружено: {data.Key}")
                : (false, $"upload HTTP {(int)putResp.StatusCode}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CloudBackupService.UploadAsync failed: " + ex.Message);
            return (false, ex.Message);
        }
    }
}
