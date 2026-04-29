using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// MC Server List Ping (handshake state=1, status request).
/// Implements the modern protocol used since MC 1.7+.
/// </summary>
public static class ServerPinger
{
    public class PingResult
    {
        public bool Ok { get; set; }
        public string Motd { get; set; } = "";
        public string Version { get; set; } = "";
        public int OnlinePlayers { get; set; }
        public int MaxPlayers { get; set; }
        public long LatencyMs { get; set; }
        public string? Error { get; set; }
    }

    public static async Task<PingResult> PingAsync(string address, int defaultPort = 25565,
        int timeoutMs = 3000, int protocol = 754 /* 1.16.5 */)
    {
        var (host, port) = SplitHost(address, defaultPort);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            using var cts = new CancellationTokenSource(timeoutMs);
            await tcp.ConnectAsync(host, port, cts.Token);
            using var stream = tcp.GetStream();

            // Handshake packet: 0x00 + protocol VarInt + host string + port + state=1
            using var hand = new MemoryStream();
            WriteVarInt(hand, 0x00);
            WriteVarInt(hand, protocol);
            WriteString(hand, host);
            hand.WriteByte((byte)((port >> 8) & 0xFF));
            hand.WriteByte((byte)(port & 0xFF));
            WriteVarInt(hand, 1); // status
            WritePacket(stream, hand.ToArray());

            // Status request: empty 0x00
            using var req = new MemoryStream();
            WriteVarInt(req, 0x00);
            WritePacket(stream, req.ToArray());

            var len = ReadVarInt(stream);
            var pid = ReadVarInt(stream);
            if (pid != 0x00) return new PingResult { Error = "unexpected packet" };
            var jsonLen = ReadVarInt(stream);
            var buf = new byte[jsonLen];
            int read = 0;
            while (read < jsonLen)
            {
                var got = await stream.ReadAsync(buf.AsMemory(read, jsonLen - read), cts.Token);
                if (got <= 0) break;
                read += got;
            }
            var json = Encoding.UTF8.GetString(buf, 0, read);
            sw.Stop();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new PingResult
            {
                Ok = true,
                LatencyMs = sw.ElapsedMilliseconds,
                Motd = ParseMotd(root),
                Version = root.TryGetProperty("version", out var v) && v.TryGetProperty("name", out var vn) ? vn.GetString() ?? "" : "",
                OnlinePlayers = root.TryGetProperty("players", out var p) && p.TryGetProperty("online", out var po) ? po.GetInt32() : 0,
                MaxPlayers = root.TryGetProperty("players", out var pm) && pm.TryGetProperty("max", out var mp) ? mp.GetInt32() : 0,
            };
        }
        catch (Exception ex)
        {
            return new PingResult { Error = ex.Message };
        }
    }

    private static (string, int) SplitHost(string addr, int def)
    {
        var i = addr.LastIndexOf(':');
        if (i > 0 && int.TryParse(addr[(i + 1)..], out var port)) return (addr[..i], port);
        return (addr, def);
    }

    private static string ParseMotd(JsonElement root)
    {
        if (!root.TryGetProperty("description", out var d)) return "";
        if (d.ValueKind == JsonValueKind.String) return d.GetString() ?? "";
        if (d.TryGetProperty("text", out var t)) return t.GetString() ?? "";
        return d.ToString();
    }

    private static void WriteVarInt(Stream s, int value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            s.WriteByte(b);
        } while (value != 0);
    }

    private static int ReadVarInt(Stream s)
    {
        int result = 0, shift = 0;
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 35) throw new InvalidDataException("VarInt too big");
        }
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(s, bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WritePacket(Stream s, byte[] data)
    {
        using var prefixed = new MemoryStream();
        WriteVarInt(prefixed, data.Length);
        prefixed.Write(data, 0, data.Length);
        var bytes = prefixed.ToArray();
        s.Write(bytes, 0, bytes.Length);
    }
}
