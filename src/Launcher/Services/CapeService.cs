using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Cape management for premium accounts via Microsoft Auth (MSA).
/// Uses Mojang services API. Requires a valid bearer token from MSA flow.
/// </summary>
public static class CapeService
{
    private const string Api = "https://api.minecraftservices.com/minecraft/profile/capes";

    public class Cape
    {
        public string Id { get; set; } = "";
        public string State { get; set; } = "";
        public string Url { get; set; } = "";
        public string Alias { get; set; } = "";
    }

    public class ProfileResp
    {
        public System.Collections.Generic.List<Cape> Capes { get; set; } = new();
    }

    public static async Task<System.Collections.Generic.IReadOnlyList<Cape>> ListAsync(string accessToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await Http.Client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return Array.Empty<Cape>();
            var data = await resp.Content.ReadFromJsonAsync<ProfileResp>();
            return data?.Capes ?? new System.Collections.Generic.List<Cape>();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CapeService.ListAsync failed: " + ex.Message);
            return Array.Empty<Cape>();
        }
    }

    public static async Task<bool> SelectAsync(string accessToken, string capeId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, "/active");
            req.RequestUri = new Uri(Api + "/active");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = JsonContent.Create(new { capeId });
            using var resp = await Http.Client.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CapeService.SelectAsync failed: " + ex.Message);
            return false;
        }
    }

    public static async Task<bool> RemoveAsync(string accessToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, Api + "/active");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await Http.Client.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CapeService.RemoveAsync failed: " + ex.Message);
            return false;
        }
    }
}
