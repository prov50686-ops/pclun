using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

public static class Http
{
    public static HttpClient Client { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        var c = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PcLun-Launcher/0.1 (+https://github.com/prov50686-ops/pclun)");
        return c;
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public static async Task DownloadFileAsync(
        string url,
        string destPath,
        string? expectedSha1 = null,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(destPath) && expectedSha1 is not null)
        {
            if (string.Equals(await Sha1OfFileAsync(destPath, ct).ConfigureAwait(false), expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new FileInfo(destPath).Length);
                return;
            }
        }

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = destPath + ".part";

        using (var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = File.Create(tmp);
            var buffer = new byte[81920];
            int n;
            long total = 0;
            while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                total += n;
                progress?.Report(total);
            }
        }

        if (expectedSha1 is not null)
        {
            var actual = await Sha1OfFileAsync(tmp, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tmp);
                throw new InvalidDataException($"SHA-1 mismatch for {url}: expected {expectedSha1}, got {actual}");
            }
        }

        if (File.Exists(destPath)) File.Delete(destPath);
        File.Move(tmp, destPath);
    }

    public static async Task<string> Sha1OfFileAsync(string path, CancellationToken ct = default)
    {
        using var sha = SHA1.Create();
        await using var fs = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return ToHex(hash);
    }

    public static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
