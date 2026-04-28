using System;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Считает онлайн через мини-бэкенд. Если бэкенд недоступен — фоллбэк на локальный счётчик.
/// </summary>
public static class OnlineService
{
    public const string BackendUrl = "https://pclun-online-trloyqbz.fly.dev";

    public record OnlineResponse(
        [property: JsonPropertyName("online")] int Online,
        [property: JsonPropertyName("total")] long Total);

    public static async Task<int> PingAsync(string username, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.Client.PostAsJsonAsync($"{BackendUrl}/heartbeat",
                new { username }, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return -1;
            var data = await resp.Content.ReadFromJsonAsync<OnlineResponse>(cancellationToken: ct).ConfigureAwait(false);
            return data?.Online ?? -1;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Online ping failed: " + ex.Message);
            return -1;
        }
    }
}
