using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Тонкая обёртка над эндпоинтами бэкенда: news, featured servers, leaderboard, stats.
/// Все ошибки логируем и возвращаем пустые списки — UI остаётся юзабельным даже без сети.
/// Используем <see cref="OnlineService.IsAvailable"/> как circuit breaker, чтобы не дёргать
/// дохлый бэкенд раз в минуту и не засорять лог.
/// </summary>
public static class RemoteCatalog
{
    public record NewsItem(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("tag")] string? Tag);

    public record NewsResponse([property: JsonPropertyName("items")] List<NewsItem> Items);

    public record ServerItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("tag")] string? Tag);

    public record ServersResponse([property: JsonPropertyName("items")] List<ServerItem> Items);

    public record LeaderboardItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sessions")] int Sessions,
        [property: JsonPropertyName("last_seen")] double LastSeen);

    public record LeaderboardResponse([property: JsonPropertyName("items")] List<LeaderboardItem> Items);

    public record StatsResponse(
        [property: JsonPropertyName("online")] int Online,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("today")] int Today,
        [property: JsonPropertyName("peak")] int Peak);

    public static async Task<List<NewsItem>> FetchNewsAsync(CancellationToken ct = default)
    {
        if (!OnlineService.IsAvailable) return new();
        try
        {
            var resp = await Http.Client.GetFromJsonAsync<NewsResponse>($"{OnlineService.BackendUrl}/news", ct).ConfigureAwait(false);
            return resp?.Items ?? new();
        }
        catch (Exception ex)
        {
            OnlineService.MarkUnavailable(ex.Message);
            return new();
        }
    }

    public static async Task<List<ServerItem>> FetchServersAsync(CancellationToken ct = default)
    {
        if (!OnlineService.IsAvailable) return new();
        try
        {
            var resp = await Http.Client.GetFromJsonAsync<ServersResponse>($"{OnlineService.BackendUrl}/servers/featured", ct).ConfigureAwait(false);
            return resp?.Items ?? new();
        }
        catch (Exception ex)
        {
            OnlineService.MarkUnavailable(ex.Message);
            return new();
        }
    }

    public static async Task<List<LeaderboardItem>> FetchLeaderboardAsync(int limit = 20, CancellationToken ct = default)
    {
        if (!OnlineService.IsAvailable) return new();
        try
        {
            var resp = await Http.Client.GetFromJsonAsync<LeaderboardResponse>($"{OnlineService.BackendUrl}/leaderboard?limit={limit}", ct).ConfigureAwait(false);
            return resp?.Items ?? new();
        }
        catch (Exception ex)
        {
            OnlineService.MarkUnavailable(ex.Message);
            return new();
        }
    }

    public static async Task<StatsResponse?> FetchStatsAsync(CancellationToken ct = default)
    {
        if (!OnlineService.IsAvailable) return null;
        try
        {
            return await Http.Client.GetFromJsonAsync<StatsResponse>($"{OnlineService.BackendUrl}/stats", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnlineService.MarkUnavailable(ex.Message);
            return null;
        }
    }
}
