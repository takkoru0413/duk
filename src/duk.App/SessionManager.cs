using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace duk.App;

public enum SessionRole { None, Host, Member }

public class SessionManager : IDisposable
{
    // Cloudflare Workers のURL（後でデプロイしたURLに変える）
    public const string SignalingUrl  = "wss://duk-signaling.dukapp.workers.dev";
    public const string InviteBaseUrl = "https://duk-signaling.dukapp.workers.dev/join";

    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    private string? _roomCode;
    private SessionRole _role = SessionRole.None;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string>? MemberJoined;
    public event EventHandler<string>? MemberLeft;
    public event EventHandler<string>? TextChanged;
    public event EventHandler?          Connected;
    public event EventHandler?          Disconnected;

    public SessionRole Role     => _role;
    public string?     RoomCode => _roomCode;
    public bool        IsActive => _ws?.State == WebSocketState.Open;

    public string InviteLink => $"{InviteBaseUrl}?room={_roomCode}";

    // ========== ホスト: セッション作成 ==========

    public async Task<string> CreateSessionAsync(string userName)
    {
        _roomCode = GenerateRoomCode();
        _role     = SessionRole.Host;
        await ConnectAsync(userName);
        await SendAsync(new { type = "create", room = _roomCode, name = userName });
        return _roomCode;
    }

    // ========== メンバー: セッション参加 ==========

    public async Task JoinSessionAsync(string roomCode, string userName)
    {
        _roomCode = roomCode;
        _role     = SessionRole.Member;
        await ConnectAsync(userName);
        await SendAsync(new { type = "join", room = _roomCode, name = userName });
    }

    // ========== テキスト変更を送信 ==========

    public async Task SendTextChangeAsync(string text, int version)
    {
        if (!IsActive) return;
        await SendAsync(new { type = "text", room = _roomCode, text, version });
    }

    // ========== セッション終了 ==========

    public async Task LeaveAsync()
    {
        if (_ws == null) return;
        await SendAsync(new { type = "leave", room = _roomCode });
        _cts.Cancel();
        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        _role     = SessionRole.None;
        _roomCode = null;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    // ========== 内部 ==========

    private async Task ConnectAsync(string userName)
    {
        _cts  = new CancellationTokenSource();
        _ws   = new ClientWebSocket();
        _ws.Options.SetRequestHeader("X-User", userName);

        await _ws.ConnectAsync(new Uri(SignalingUrl), _cts.Token);
        Connected?.Invoke(this, EventArgs.Empty);
        _ = ReceiveLoopAsync();
    }

    private async Task ReceiveLoopAsync()
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (_ws!.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buf, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                HandleMessage(json);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void HandleMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();
            MessageReceived?.Invoke(this, json);

            switch (type)
            {
                case "join":
                    MemberJoined?.Invoke(this, doc.RootElement.GetProperty("name").GetString() ?? "");
                    break;
                case "leave":
                    MemberLeft?.Invoke(this, doc.RootElement.GetProperty("name").GetString() ?? "");
                    break;
                case "text":
                    var text = doc.RootElement.GetProperty("text").GetString() ?? "";
                    TextChanged?.Invoke(this, text);
                    break;
            }
        }
        catch { }
    }

    private async Task SendAsync(object obj)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(obj);
        var data = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(data, WebSocketMessageType.Text, true, _cts.Token);
    }

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng  = System.Security.Cryptography.RandomNumberGenerator.Create();
        var buf  = new byte[9];
        rng.GetBytes(buf);
        return $"{Encode(buf[0..3], chars)}-{Encode(buf[3..6], chars)}-{Encode(buf[6..9], chars)}";
    }

    private static string Encode(byte[] bytes, string chars) =>
        new(bytes.Select(b => chars[b % chars.Length]).ToArray());

    public void Dispose()
    {
        _cts.Cancel();
        _ws?.Dispose();
    }
}
