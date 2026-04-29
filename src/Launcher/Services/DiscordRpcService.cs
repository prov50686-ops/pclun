using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// Очень компактная реализация Discord Rich Presence без сторонних зависимостей.
/// Использует именованный pipe (Windows) или unix socket (Linux/macOS) к `discord-ipc-{0..9}`.
/// Если Discord не запущен — все методы становятся no-op.
///
/// Application id: открытое приложение PcLun (можно подменить через env DISCORD_APP_ID).
/// </summary>
public static class DiscordRpcService
{
    private const string DefaultAppId = "1364000000000000000"; // placeholder; can be overridden via env
    private static Stream? _stream;
    private static int _pid;
    private static DateTimeOffset _startTime;

    public static bool Enabled { get; private set; }

    public static async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (Enabled) return true;
        var appId = Environment.GetEnvironmentVariable("DISCORD_APP_ID") ?? DefaultAppId;
        try
        {
            _stream = await OpenIpcAsync(ct).ConfigureAwait(false);
            if (_stream is null) return false;
            _pid = Process.GetCurrentProcess().Id;
            await SendAsync(0, JsonSerializer.SerializeToUtf8Bytes(new
            {
                v = 1,
                client_id = appId,
                nonce = Guid.NewGuid().ToString()
            }), ct).ConfigureAwait(false);
            await ReadFrameAsync(ct).ConfigureAwait(false); // handshake response
            _startTime = DateTimeOffset.UtcNow;
            Enabled = true;
            AppLogger.Info("Discord RPC connected.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Discord RPC connect failed: " + ex.Message);
            await DisconnectAsync().ConfigureAwait(false);
            return false;
        }
    }

    public static async Task SetActivityAsync(string state, string details, CancellationToken ct = default)
    {
        if (!Enabled || _stream is null) return;
        try
        {
            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = _pid,
                    activity = new
                    {
                        details = details,
                        state = state,
                        timestamps = new { start = _startTime.ToUnixTimeSeconds() },
                        assets = new
                        {
                            large_image = "pclun",
                            large_text = "PcLun by MrDomik",
                        }
                    }
                },
                nonce = Guid.NewGuid().ToString()
            };
            await SendAsync(1, JsonSerializer.SerializeToUtf8Bytes(payload), ct).ConfigureAwait(false);
            await ReadFrameAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Discord RPC SetActivity failed: " + ex.Message);
            await DisconnectAsync().ConfigureAwait(false);
        }
    }

    public static async Task DisconnectAsync()
    {
        Enabled = false;
        if (_stream is not null)
        {
            try { await _stream.DisposeAsync().ConfigureAwait(false); } catch { }
            _stream = null;
        }
    }

    private static async Task SendAsync(int opcode, byte[] data, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("not connected");
        var header = new byte[8];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), opcode);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), data.Length);
        await _stream.WriteAsync(header, ct).ConfigureAwait(false);
        await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<(int op, string body)> ReadFrameAsync(CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("not connected");
        var header = new byte[8];
        await ReadExactAsync(_stream, header, ct).ConfigureAwait(false);
        var op = BitConverter.ToInt32(header, 0);
        var len = BitConverter.ToInt32(header, 4);
        var buf = new byte[len];
        await ReadExactAsync(_stream, buf, ct).ConfigureAwait(false);
        return (op, Encoding.UTF8.GetString(buf));
    }

    private static async Task ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(read, buf.Length - read), ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("Discord IPC closed prematurely");
            read += n;
        }
    }

    private static async Task<Stream?> OpenIpcAsync(CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(500, ct).ConfigureAwait(false);
                    return pipe;
                }
                else
                {
                    var path = ResolveUnixPath(i);
                    if (path is null || !File.Exists(path)) continue;
                    var sock = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,
                        System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
                    await sock.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(path), ct).ConfigureAwait(false);
                    return new System.Net.Sockets.NetworkStream(sock, ownsSocket: true);
                }
            }
            catch
            {
                // try next index
            }
        }
        return null;
    }

    private static string? ResolveUnixPath(int idx)
    {
        var tmp = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                  ?? Environment.GetEnvironmentVariable("TMPDIR")
                  ?? "/tmp";
        var candidate = Path.Combine(tmp, $"discord-ipc-{idx}");
        if (File.Exists(candidate)) return candidate;
        // flatpak/snap layout
        foreach (var sub in new[] { "app/com.discordapp.Discord", "snap.discord" })
        {
            var p = Path.Combine(tmp, sub, $"discord-ipc-{idx}");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
