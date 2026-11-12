// 基于 MCTier 项目开发。原作者：MCTier Team。
// https://github.com/pmh1314520/MCTier — 原许可见 Codes/Licenses/MCTier/LICENSE。
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BCCL.Core.Link.MCTier;

public sealed class MCTierDirectoryException(string message) : IOException(message)
{
    public bool NameOccupied => Message.Contains("占用", StringComparison.Ordinal) || Message.Contains("已存在", StringComparison.Ordinal);
}

/// <summary>
/// A real, private v3 membership reserves a name atomically. A preview is retained
/// until creation or explicitly released; no password guesses or public-list heuristics.
/// </summary>
public sealed class MCTierDirectoryLease : IDisposable, IAsyncDisposable
{
    public const string DefaultEndpoint = "wss://mctier.pmhs.top/signaling";
    private readonly ClientWebSocket socket;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly byte[] privateKey;
    private int disposed;
    public MCTierRoom Room { get; }
    public Uri Endpoint { get; }
    public string VirtualIp { get; }
    public bool IsAlive { get; private set; } = true;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action? Closed;

    private MCTierDirectoryLease(ClientWebSocket socket, MCTierRoom room, Uri endpoint, string virtualIp, byte[] key)
    { this.socket = socket; Room = room; Endpoint = endpoint; VirtualIp = virtualIp; privateKey = key; }

    public static Uri ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0 || (uri.Scheme != "wss" && !(uri.Scheme == "ws" && uri.IsLoopback)))
            throw new ArgumentException("房间名称服务地址应以 wss:// 开头。");
        return uri;
    }

    public static Task<MCTierDirectoryLease> ReserveAsync(MCTierRoom room, string nickname, Uri endpoint, CancellationToken token) =>
        RegisterAsync(room, nickname, endpoint, "create", $"10.126.126.{RandomNumberGenerator.GetInt32(1, 255)}", null, token);

    public static Task<MCTierDirectoryLease> JoinAsync(MCTierRoom room, string nickname, Uri endpoint, string virtualIp, CancellationToken token) =>
        RegisterAsync(room, nickname, endpoint, "join", virtualIp, null, token);

    public static async Task<MCTierDirectoryLease> JoinAllocatedAsync(MCTierRoom room, string nickname, Uri endpoint, CancellationToken token)
    {
        // The directory checks address uniqueness atomically with admission. Retry only explicit address collisions.
        for (var attempt = 0; ; attempt++)
        {
            try { return await JoinAsync(room, nickname, endpoint, $"10.126.126.{RandomNumberGenerator.GetInt32(1, 255)}", token); }
            catch (MCTierDirectoryException ex) when (attempt < 7 && ex.Message.Contains("virtualIp", StringComparison.OrdinalIgnoreCase) && ex.Message.Contains("使用", StringComparison.Ordinal)) { }
        }
    }

    public async Task<MCTierDirectoryLease> ConfirmAddressAsync(string nickname, string virtualIp, CancellationToken token)
    {
        if (!IsAlive) throw new MCTierDirectoryException("名称保留已失效，请重新检测后创建。");
        // Same signed identity replaces its own preview membership, preserving the
        // reservation even while the real EasyTier address is being assigned.
        return await RegisterAsync(Room, nickname, Endpoint, "auto", virtualIp, privateKey, token);
    }

    public static string CanonicalProof(string challenge, string name, string ip) =>
        $"MCTIER-SIGNALING-V3\n3\n{challenge}\n{name}\n{ip}";

    private static async Task<MCTierDirectoryLease> RegisterAsync(MCTierRoom room, string nickname, Uri endpoint,
        string mode, string ip, byte[]? existingKey, CancellationToken token)
    {
        room.Validate();
        ValidateEndpoint(endpoint.AbsoluteUri);
        if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 32 || nickname.Any(char.IsControl))
            throw new ArgumentException("请填写有效的玩家昵称（1–32 个字符）。");
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !ip.StartsWith("10.126.126.", StringComparison.Ordinal) || address.GetAddressBytes()[3] is 0 or 255)
            throw new MCTierDirectoryException("名称服务需要 10.126.126.x 虚拟地址。请使用直接组网连接其他网段。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        try
        {
            await ws.ConnectAsync(endpoint, timeout.Token);
            using var challenge = await ReadAsync(ws, timeout.Token);
            var root = challenge.RootElement;
            if (Text(root, "type") != "server-challenge" || !root.TryGetProperty("protocolVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var protocolVersion) || protocolVersion != 3 ||
                !root.TryGetProperty("lobbyEntryModes", out var modes) || modes.ValueKind != JsonValueKind.True)
                throw new MCTierDirectoryException("名称服务不支持重名保护，请更换服务或选择直接组网。");
            var nonce = Text(root, "challenge");
            if (nonce.Length != 64 || !nonce.All(Uri.IsHexDigit)) throw new MCTierDirectoryException("名称服务返回的验证信息无效。");
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            if (existingKey is not null) key.ImportPkcs8PrivateKey(existingKey, out _);
            var publicKey = key.ExportSubjectPublicKeyInfo();
            var proof = key.SignData(Encoding.UTF8.GetBytes(CanonicalProof(nonce, room.Name, ip)), HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
            var request = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "register-v3", protocolVersion = 3, entryMode = mode,
                playerName = nickname, virtualIp = ip, lobbyName = room.Name,
                lobbyPassword = room.NetworkPassword, clientVersion = "3.10.0", useDomain = false,
                identityPublicKey = Convert.ToBase64String(publicKey), challengeSignature = Convert.ToBase64String(proof)
            });
            await ws.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            using var response = await ReadAsync(ws, timeout.Token);
            var reply = response.RootElement;
            if (Text(reply, "type") != "register-success")
            {
                var message = Text(reply, "message");
                throw new MCTierDirectoryException(string.IsNullOrWhiteSpace(message) ? "名称服务未确认房间，请重试。" :
                    new string(message.Where(c => !char.IsControl(c)).Take(200).ToArray()));
            }
            var expectedId = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
            var expectedRoom = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(room.Name))).ToLowerInvariant();
            if (Text(reply, "clientId") != expectedId || Text(reply, "lobbyId") != expectedRoom)
                throw new MCTierDirectoryException("名称服务返回的房间身份不匹配。");
            var lease = new MCTierDirectoryLease(ws, room, endpoint, ip, key.ExportPkcs8PrivateKey());
            lease.Completion = lease.RunAsync();
            return lease;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { ws.Dispose(); throw new MCTierDirectoryException("名称检测超时，暂时无法确认是否重名，请重新检测。"); }
        catch (WebSocketException)
        { ws.Dispose(); throw new MCTierDirectoryException("无法连接房间名称服务，暂时无法确认是否重名。"); }
        catch { ws.Dispose(); throw; }
    }

    private async Task RunAsync()
    {
        var heartbeat = HeartbeatAsync(stop.Token);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                receiveTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                using var frame = await ReadAsync(socket, receiveTimeout.Token);
                var type = Text(frame.RootElement, "type");
                if (type is "kicked" or "register-error" or "version-too-old") break;
            }
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException or ObjectDisposedException or JsonException) { }
        finally
        {
            IsAlive = false;
            stop.Cancel();
            socket.Abort();
            try { await heartbeat; } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
            if (Volatile.Read(ref disposed) == 0) Closed?.Invoke();
        }
    }

    private async Task HeartbeatAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), token);
                await sendGate.WaitAsync(token);
                try { await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"ping\"}").AsMemory(), WebSocketMessageType.Text, true, token); }
                finally { sendGate.Release(); }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { socket.Abort(); }
    }

    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static async Task<JsonDocument> ReadAsync(WebSocket ws, CancellationToken token)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType != WebSocketMessageType.Text) throw new IOException("名称服务连接已关闭或返回了无效内容。");
            if (stream.Length + result.Count > 262144) throw new IOException("名称服务返回的内容过长。");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                var document = JsonDocument.Parse(stream.ToArray());
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                { document.Dispose(); throw new IOException("名称服务返回的消息格式无效。"); }
                return document;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        IsAlive = false;
        stop.Cancel();
        socket.Abort();
        socket.Dispose();
        CryptographicOperations.ZeroMemory(privateKey);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        IsAlive = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await sendGate.WaitAsync(timeout.Token);
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Leaving room", timeout.Token);
            }
            finally { sendGate.Release(); }
            await Completion.WaitAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            stop.Cancel();
            socket.Abort();
            socket.Dispose();
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }
}
