using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

public record AuthAccount(
    [property: JsonPropertyName("type")] string Type,            // "offline" | "msa"
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("uuid")] string Uuid,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);

public static class AuthService
{
    /// <summary>
    /// Создаёт offline-аккаунт по нику. UUID считается детерминированно (как у официальных пиратских лаунчеров).
    /// </summary>
    public static AuthAccount Offline(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname)) nickname = "Player";
        return new AuthAccount("offline", nickname.Trim(), OfflineUuid(nickname), "0", null);
    }

    public static string OfflineUuid(string nickname)
    {
        // Mojang uses MD5 of "OfflinePlayer:" + name with version 3 UUID encoding.
        var bytes = Encoding.UTF8.GetBytes("OfflinePlayer:" + nickname);
        var hash = MD5.HashData(bytes);
        // set version (3) and variant
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash).ToString();
    }

    public static void Save(AuthAccount account)
    {
        try
        {
            var json = JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Paths.AccountsFile, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save account", ex);
        }
    }

    public static AuthAccount? LoadSaved()
    {
        try
        {
            if (!File.Exists(Paths.AccountsFile)) return null;
            var text = File.ReadAllText(Paths.AccountsFile);
            return JsonSerializer.Deserialize<AuthAccount>(text);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load account", ex);
            return null;
        }
    }

    // ---------- Microsoft Device Code Flow ----------
    // Используем публичный client_id, разрешённый Mojang (как в Prism / MultiMC).
    // Это позволяет пользователю войти под своим Microsoft-аккаунтом без своего Azure-приложения.
    private const string MsClientId = "00000000402b5328"; // Mojang official launcher client id (public, used by community launchers)
    private const string MsScope = "service::user.auth.xboxlive.com::MBI_SSL";

    public record DeviceCodeResponse(string user_code, string device_code, string verification_uri, int expires_in, int interval);
    public record DeviceTokenResponse(string? access_token, string? refresh_token, string? token_type, int? expires_in, string? error, string? error_description);

    public static async Task<DeviceCodeResponse> StartDeviceCodeAsync(CancellationToken ct = default)
    {
        var url = "https://login.live.com/oauth20_connect.srf";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = MsClientId,
            ["scope"] = MsScope,
            ["response_type"] = "device_code"
        });

        using var resp = await Http.Client.PostAsync(url, content, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<DeviceCodeResponse>(text)!;
    }

    public static async Task<AuthAccount> PollDeviceTokenAsync(DeviceCodeResponse code, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(code.expires_in);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(code.interval, 5)), ct).ConfigureAwait(false);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = MsClientId,
                ["device_code"] = code.device_code,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            using var resp = await Http.Client.PostAsync("https://login.live.com/oauth20_token.srf", content, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var token = JsonSerializer.Deserialize<DeviceTokenResponse>(text);
            if (token is null) continue;
            if (token.error == "authorization_pending") continue;
            if (token.error == "slow_down") { await Task.Delay(2000, ct).ConfigureAwait(false); continue; }
            if (token.error is not null) throw new InvalidOperationException($"Microsoft auth error: {token.error} — {token.error_description}");
            if (token.access_token is null) continue;

            // обмен на Xbox Live → XSTS → Minecraft → профиль
            var (mcToken, mcUuid, mcName) = await ExchangeForMinecraftAsync(token.access_token, ct).ConfigureAwait(false);
            return new AuthAccount("msa", mcName, mcUuid, mcToken, token.refresh_token);
        }

        throw new TimeoutException("Истёк срок действия кода. Попробуйте ещё раз.");
    }

    private static async Task<(string McAccessToken, string Uuid, string Name)> ExchangeForMinecraftAsync(string msAccessToken, CancellationToken ct)
    {
        // 1) Xbox Live
        var xboxBody = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = msAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        var xboxResp = await Http.Client.PostAsJsonAsync("https://user.auth.xboxlive.com/user/authenticate", xboxBody, ct).ConfigureAwait(false);
        xboxResp.EnsureSuccessStatusCode();
        var xboxJson = JsonDocument.Parse(await xboxResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var xblToken = xboxJson.RootElement.GetProperty("Token").GetString();
        var uhs = xboxJson.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString();

        // 2) XSTS
        var xstsBody = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };
        var xstsResp = await Http.Client.PostAsJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", xstsBody, ct).ConfigureAwait(false);
        xstsResp.EnsureSuccessStatusCode();
        var xstsJson = JsonDocument.Parse(await xstsResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var xstsToken = xstsJson.RootElement.GetProperty("Token").GetString();

        // 3) Minecraft auth
        var mcBody = new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" };
        var mcResp = await Http.Client.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", mcBody, ct).ConfigureAwait(false);
        mcResp.EnsureSuccessStatusCode();
        var mcJson = JsonDocument.Parse(await mcResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var mcAccessToken = mcJson.RootElement.GetProperty("access_token").GetString()!;

        // 4) Profile
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);
        var profResp = await Http.Client.SendAsync(req, ct).ConfigureAwait(false);
        profResp.EnsureSuccessStatusCode();
        var prof = JsonDocument.Parse(await profResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var id = prof.RootElement.GetProperty("id").GetString()!;
        var name = prof.RootElement.GetProperty("name").GetString()!;
        // id — без дефисов; добавим
        var uuid = Guid.ParseExact(id, "N").ToString();

        return (mcAccessToken, uuid, name);
    }
}
