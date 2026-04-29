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

    /// <summary>
    /// После первой ошибки от бэкенда даём ему «отдохнуть» 5 минут — это убирает
    /// спам в логе у пользователей с выключенным/недоступным бэкендом и не мешает
    /// автоматическому восстановлению, когда бэкенд снова поднимется.
    /// </summary>
    private static DateTime _backoffUntil = DateTime.MinValue;
    private static readonly TimeSpan BackoffDuration = TimeSpan.FromMinutes(5);
    private static bool _firstFailureLogged;

    public static bool IsAvailable => DateTime.UtcNow >= _backoffUntil;

    public record OnlineResponse(
        [property: JsonPropertyName("online")] int Online,
        [property: JsonPropertyName("total")] long Total);

    public static async Task<int> PingAsync(string username, CancellationToken ct = default)
    {
        if (!IsAvailable) return -1;
        try
        {
            using var resp = await Http.Client.PostAsJsonAsync($"{BackendUrl}/heartbeat",
                new { username }, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                MarkUnavailable($"HTTP {(int)resp.StatusCode}");
                return -1;
            }
            var data = await resp.Content.ReadFromJsonAsync<OnlineResponse>(cancellationToken: ct).ConfigureAwait(false);
            return data?.Online ?? -1;
        }
        catch (Exception ex)
        {
            MarkUnavailable(ex.Message);
            return -1;
        }
    }

    /// <summary>
    /// Помечаем бэкенд как недоступный на BackoffDuration. Логируем только первый раз
    /// за сессию, чтобы не засорять лог одинаковыми WARN'ами.
    /// </summary>
    public static void MarkUnavailable(string reason)
    {
        _backoffUntil = DateTime.UtcNow + BackoffDuration;
        if (!_firstFailureLogged)
        {
            _firstFailureLogged = true;
            AppLogger.Info($"Online backend недоступен ({reason}). Повтор через {BackoffDuration.TotalMinutes:0} мин.");
        }
    }
}
