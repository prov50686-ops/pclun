using System;
using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcLun.Services;

/// <summary>
/// WebSocket client to backend /chat. Best-effort: never throws to caller.
/// </summary>
public class ChatClient : IDisposable
{
    private readonly Uri _uri;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public class ChatMessage
    {
        public string Nick { get; set; } = "";
        public string Text { get; set; } = "";
        public bool System { get; set; }
        public DateTime At { get; set; } = DateTime.Now;

        public string Display => System ? $"[*] {Text}" : $"<{Nick}> {Text}";
    }

    public ChatClient(string baseUrl, string nick)
    {
        var b = baseUrl.TrimEnd('/');
        if (b.StartsWith("https://")) b = "wss://" + b.Substring("https://".Length);
        else if (b.StartsWith("http://")) b = "ws://" + b.Substring("http://".Length);
        _uri = new Uri($"{b}/chat?nick={Uri.EscapeDataString(nick)}");
    }

    public async Task ConnectAsync()
    {
        Disconnect();
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        try
        {
            await _ws.ConnectAsync(_uri, _cts.Token);
            _ = Task.Run(() => ReceiveLoop(_ws, _cts.Token));
        }
        catch (Exception ex) { AppLogger.Warn("ChatClient.Connect failed: " + ex.Message); }
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
    {
        var buf = new byte[4096];
        var sb = new StringBuilder();
        try
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(buf, token);
                    if (res.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                } while (!res.EndOfMessage);

                using var doc = JsonDocument.Parse(sb.ToString());
                var root = doc.RootElement;
                var msg = new ChatMessage
                {
                    Nick = root.TryGetProperty("nick", out var n) ? n.GetString() ?? "" : "",
                    Text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                    System = root.TryGetProperty("system", out var s) && s.GetBoolean(),
                };
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Messages.Add(msg));
            }
        }
        catch (Exception ex) { AppLogger.Warn("ChatClient.ReceiveLoop: " + ex.Message); }
    }

    public async Task SendAsync(string text)
    {
        if (_ws is null || _ws.State != WebSocketState.Open) return;
        try
        {
            var json = JsonSerializer.Serialize(new { text });
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts!.Token);
        }
        catch (Exception ex) { AppLogger.Warn("ChatClient.SendAsync: " + ex.Message); }
    }

    public void Disconnect()
    {
        try
        {
            _cts?.Cancel();
            _ws?.Abort();
            _ws?.Dispose();
        }
        catch { /* ignore */ }
        _ws = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Disconnect();
}
