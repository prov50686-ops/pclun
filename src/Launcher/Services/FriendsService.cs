using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;

namespace PcLun.Services;

/// <summary>
/// Friends list synced with the backend. Local cache fallback when offline.
/// </summary>
public static class FriendsService
{
    public class Friend
    {
        public string Name { get; set; } = "";
        public bool Online { get; set; }
        public int Sessions { get; set; }
        public double Last_seen { get; set; }
    }

    private class ListResp { public List<Friend> Items { get; set; } = new(); }

    public static string Base => OnlineService.BackendUrl;

    public static async Task<IReadOnlyList<Friend>> ListAsync(string nick)
    {
        try
        {
            var resp = await Http.Client.GetFromJsonAsync<ListResp>(
                $"{Base}/friends/{HttpUtility.UrlEncode(nick)}");
            return resp?.Items ?? new List<Friend>();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FriendsService.ListAsync failed: " + ex.Message);
            return Array.Empty<Friend>();
        }
    }

    public static async Task<bool> AddAsync(string owner, string friend)
    {
        try
        {
            using var resp = await Http.Client.PostAsJsonAsync($"{Base}/friends/add", new { owner, friend });
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FriendsService.AddAsync failed: " + ex.Message);
            return false;
        }
    }

    public static async Task<bool> RemoveAsync(string owner, string friend)
    {
        try
        {
            using var resp = await Http.Client.PostAsJsonAsync($"{Base}/friends/remove", new { owner, friend });
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FriendsService.RemoveAsync failed: " + ex.Message);
            return false;
        }
    }
}
