using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BCCL.Core.Link.MCTier;

public sealed record ExtrasPacket(string Kind, string Id = "", string Text = "", long Size = 0,
    long Offset = 0, byte[]? Data = null, double X = 0, double Y = 0, int Code = 0);
public sealed record ExtrasPeer(string Ip, string Name) { public override string ToString() => $"{Name} · {Ip}"; }
public sealed record ExtrasFile(string PeerIp, string Id, string Name, long Size);

/// <summary>BCCL room services, carried by EasyTier's encrypted network, with mutual room authentication.</summary>
public sealed class MCTierExtrasHub : IDisposable
{
    public const int Port = 39257;
    private const int MaxFrame = 2 * 1024 * 1024;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim slots = new(32, 32);
    private readonly byte[] key;
    private readonly string nickname;
    private readonly ConcurrentDictionary<string, Connection> connections = new();
    private readonly ConcurrentDictionary<string, byte> pending = new();
    private readonly ConcurrentDictionary<string, string> shared = new();
    private readonly ConcurrentDictionary<string, Download> downloads = new();
    private readonly ConcurrentDictionary<string, ExtrasFile> offers = new();
    public string LocalAddress { get; set; } = "";
    public int ListeningPort => ((IPEndPoint)listener.LocalEndpoint).Port;
    public IReadOnlyList<ExtrasPeer> Peers => connections.Values.Select(c => c.Peer).ToArray();
    public IReadOnlyList<ExtrasFile> Files => offers.Values.ToArray();
    public event Action? Changed;
    public event Action<ExtrasPeer, ExtrasPacket>? Received;
    public event Action<string>? Notice;

    public MCTierExtrasHub(MCTierRoom room, string nickname, IPAddress? bind = null, int port = Port)
    {
        key = SHA256.HashData(Encoding.UTF8.GetBytes("BCCL-extras-v1\0" + room.Name + "\0" + room.NetworkPassword));
        this.nickname = nickname;
        listener = new TcpListener(bind ?? IPAddress.Loopback, port);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start(16);
        _ = Task.Run(AcceptAsync);
        _ = Task.Run(KeepAliveAsync);
    }

    private async Task KeepAliveAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(5000, stop.Token);
                foreach (var connection in connections.Values)
                {
                    if (Environment.TickCount64 - Interlocked.Read(ref connection.LastSeen) > 20000) { connection.Dispose(); continue; }
                    try { await connection.SendAsync(new ExtrasPacket("ping")); }
                    catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { connection.Dispose(); }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                _ = AttachAsync(client, null);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    public void Discover(IReadOnlyList<MCTierPeer> peers, int socksPort)
    {
        LocalAddress = peers.FirstOrDefault(p => p.IsLocal)?.Ip ?? "";
        if (!IPAddress.TryParse(LocalAddress, out _)) return;
        foreach (var peer in peers.Where(p => !p.IsLocal && string.CompareOrdinal(LocalAddress, p.Ip) < 0))
            if (!connections.ContainsKey(peer.Ip) && pending.TryAdd(peer.Ip, 0)) _ = Task.Run(() => ConnectProxyAsync(peer.Ip, socksPort));
    }

    private async Task ConnectProxyAsync(string ip, int socksPort)
    {
        MCTierForwarder? bridge = null;
        TcpClient? client = null;
        try
        {
            bridge = new MCTierForwarder(socksPort, ip, Port);
            client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(IPAddress.Loopback, bridge.LocalPort, timeout.Token);
            await AttachAsync(client, bridge);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { client?.Dispose(); bridge?.Dispose(); }
        finally { pending.TryRemove(ip, out _); }
    }

    // Also used by diagnostics to exercise the exact wire protocol without a virtual adapter.
    public async Task ConnectDirectAsync(string host, int port, CancellationToken token)
    {
        var client = new TcpClient();
        try { await client.ConnectAsync(host, port, token); _ = AttachAsync(client, null); }
        catch { client.Dispose(); throw; }
    }

    private async Task AttachAsync(TcpClient client, IDisposable? bridge)
    {
        if (!slots.Wait(0)) { client.Dispose(); bridge?.Dispose(); return; }
        Connection? connection = null;
        try
        {
            client.NoDelay = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var stream = client.GetStream();
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await WriteAsync(stream, new ExtrasPacket("hello", nonce, nickname, Data: Encoding.ASCII.GetBytes(LocalAddress)), timeout.Token);
            var hello = await ReadAsync(stream, timeout.Token);
            if (hello.Kind != "hello" || hello.Id.Length != 64 || hello.Text.Length is < 1 or > 32 || hello.Data is null || hello.Data.Length > 15)
                throw new IOException("联机功能握手无效。");
            var ip = Encoding.ASCII.GetString(hello.Data);
            if (!IPAddress.TryParse(ip, out _) || ip == LocalAddress) throw new IOException("成员地址无效。");
            byte[] Sign(string first, string second) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(first + ":" + second));
            await WriteAsync(stream, new ExtrasPacket("auth", Data: Sign(nonce, hello.Id)), timeout.Token);
            var auth = await ReadAsync(stream, timeout.Token);
            if (auth.Kind != "auth" || auth.Data is null || !CryptographicOperations.FixedTimeEquals(auth.Data, Sign(hello.Id, nonce)))
                throw new IOException("房间鉴权失败。");
            connection = new Connection(client, new ExtrasPeer(ip, hello.Text), bridge, stop.Token);
            if (!connections.TryAdd(ip, connection)) return;
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            Changed?.Invoke();
            foreach (var file in shared) await OfferAsync(connection, file.Key, file.Value);
            while (!stop.IsCancellationRequested)
            {
                var packet = await ReadAsync(stream, stop.Token);
                Interlocked.Exchange(ref connection.LastSeen, Environment.TickCount64);
                await HandleAsync(connection, packet);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or JsonException or CryptographicException) { }
        finally
        {
            if (connection is not null && connections.TryRemove(new KeyValuePair<string, Connection>(connection.Peer.Ip, connection)))
            {
                foreach (var offer in offers.Where(p => p.Value.PeerIp == connection.Peer.Ip)) offers.TryRemove(offer.Key, out _);
                foreach (var download in downloads.Where(p => p.Value.Peer == connection.Peer.Ip))
                    if (downloads.TryRemove(download.Key, out var value)) value.Dispose();
                Received?.Invoke(connection.Peer, new ExtrasPacket("disconnected"));
                Changed?.Invoke();
            }
            connection?.Dispose(); client.Dispose(); bridge?.Dispose(); slots.Release();
        }
    }

    public async Task SendAsync(string ip, ExtrasPacket packet)
    {
        if (!connections.TryGetValue(ip, out var connection)) throw new IOException("成员已断开，等待其重新连接。");
        await connection.SendAsync(packet);
    }

    public async Task BroadcastAsync(ExtrasPacket packet)
    {
        foreach (var connection in connections.Values)
            try { await connection.SendAsync(packet); }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { connection.Dispose(); }
    }
    // One in-flight frame for the entire stream. Peers send concurrently so their latencies do not add up.
    public async Task BroadcastScreenAsync(ExtrasPacket packet)
    {
        if (packet.Kind != "screen") throw new ArgumentException("这里只能发送屏幕画面。");
        await Task.WhenAll(connections.Values.Select(async connection =>
        {
            try { await connection.SendAsync(packet); }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { connection.Dispose(); }
        }));
    }

    public async Task ShareFileAsync(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("找不到文件。", path);
        var id = Guid.NewGuid().ToString("N");
        shared[id] = file.FullName;
        foreach (var connection in connections.Values) await OfferAsync(connection, id, file.FullName);
    }
    public async Task ClearSharedAsync()
    {
        shared.Clear();
        foreach (var connection in connections.Values)
            foreach (var transfer in connection.Transfers.Values)
                try { transfer.Cancel(); } catch (ObjectDisposedException) { }
        await BroadcastAsync(new ExtrasPacket("file-clear"));
    }

    private static Task OfferAsync(Connection connection, string id, string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? connection.SendAsync(new ExtrasPacket("file-offer", id, file.Name, file.Length)) : Task.CompletedTask;
    }

    public async Task ReceiveFileAsync(ExtrasFile offer, string folder)
    {
        if (!offers.ContainsKey(offer.PeerIp + ":" + offer.Id)) throw new IOException("文件分享已失效。");
        if (downloads.Count >= 4) throw new IOException("最多同时接收 4 个文件，请等待完成后再接收。");
        Directory.CreateDirectory(folder);
        var download = new Download(offer, folder);
        var key = offer.PeerIp + ":" + offer.Id;
        if (!downloads.TryAdd(key, download)) { download.Dispose(); throw new IOException("该文件正在接收。"); }
        try { await SendAsync(offer.PeerIp, new ExtrasPacket("file-get", offer.Id)); }
        catch { if (downloads.TryRemove(key, out var value)) value.Dispose(); throw; }
    }
    public async Task CancelReceivingAsync()
    {
        foreach (var pair in downloads)
            if (downloads.TryRemove(pair.Key, out var value))
            {
                value.Dispose();
                try { await SendAsync(value.Peer, new ExtrasPacket("file-cancel", value.Id)); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            }
    }

    private async Task HandleAsync(Connection connection, ExtrasPacket packet)
    {
        var id = connection.Peer.Ip + ":" + packet.Id;
        switch (packet.Kind)
        {
            case "screen" when packet.Data is { Length: > 0 and <= 1_000_000 } && packet.Size is > 0 and <= 16384 && packet.Offset is > 0 and <= 16384:
                connection.LastScreen = packet;
                Received?.Invoke(connection.Peer, packet); break;
            case "screen-repeat":
                if (connection.LastScreen is not null) Received?.Invoke(connection.Peer, connection.LastScreen);
                break;
            case "screen-stop": connection.LastScreen = null; Received?.Invoke(connection.Peer, packet); break;
            case "ping": await connection.SendAsync(new ExtrasPacket("pong")); break;
            case "pong": break;
            case "file-offer":
                if (packet.Id.Length != 32 || packet.Text.Length is < 1 or > 255 || packet.Size is < 0 or > 100_000_000_000 || offers.Count >= 1024) return;
                offers[id] = new ExtrasFile(connection.Peer.Ip, packet.Id, Path.GetFileName(packet.Text), packet.Size);
                Changed?.Invoke(); break;
            case "file-get":
                if (connection.Transfers.Count < 4 && shared.TryGetValue(packet.Id, out var path))
                {
                    var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    if (connection.Transfers.TryAdd(packet.Id, cancel)) _ = SendFileAsync(connection, packet.Id, path, cancel);
                    else cancel.Dispose();
                }
                else await connection.SendAsync(new ExtrasPacket("file-error", packet.Id));
                break;
            case "file-cancel":
                if (connection.Transfers.TryGetValue(packet.Id, out var transfer))
                    try { transfer.Cancel(); } catch (ObjectDisposedException) { }
                break;
            case "file-clear":
                foreach (var offer in offers.Where(p => p.Value.PeerIp == connection.Peer.Ip)) offers.TryRemove(offer.Key, out _);
                Changed?.Invoke(); break;
            case "file-data":
                if (downloads.TryGetValue(id, out var download))
                    try { await download.WriteAsync(packet, stop.Token); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
                    { if (downloads.TryRemove(id, out var failed)) { failed.Dispose(); Notice?.Invoke("文件接收失败：" + ex.Message); } }
                break;
            case "file-end":
                if (downloads.TryRemove(id, out var complete))
                    using (complete)
                        try { Notice?.Invoke("文件已保存：" + complete.Complete(packet.Text)); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice?.Invoke("文件接收失败：" + ex.Message); }
                break;
            case "file-error":
                if (downloads.TryRemove(id, out var failedFile)) failedFile.Dispose();
                Notice?.Invoke("对方暂时无法发送该文件，请重新分享。"); break;
            case "chat" when packet.Text.Length is > 0 and <= 4000:
            case "voice" when packet.Data is { Length: > 0 and <= 6400 }:
            case "control-request":
            case "control-grant" when packet.Id.Length == 64:
            case "control-deny":
            case "control-revoke":
            case "input" when packet.Id.Length == 64 && double.IsFinite(packet.X) && double.IsFinite(packet.Y):
                Received?.Invoke(connection.Peer, packet); break;
        }
    }

    private async Task SendFileAsync(Connection connection, string id, string path, CancellationTokenSource cancel)
    {
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[8 * 1024];
            long offset = 0;
            int length;
            while ((length = await input.ReadAsync(buffer, cancel.Token)) > 0)
            {
                hash.AppendData(buffer, 0, length);
                await connection.SendAsync(new ExtrasPacket("file-data", id, Offset: offset, Data: buffer.AsSpan(0, length).ToArray()));
                offset += length;
            }
            await connection.SendAsync(new ExtrasPacket("file-end", id, Convert.ToHexString(hash.GetHashAndReset())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException)
        {
            Notice?.Invoke("文件发送已中断：" + (ex is OperationCanceledException ? "连接超时或分享已停止。" : ex.Message));
            try { await connection.SendAsync(new ExtrasPacket("file-error", id)); } catch (Exception) { }
        }
        finally { connection.Transfers.TryRemove(id, out _); cancel.Dispose(); }
    }

    private static async Task<ExtrasPacket> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaxFrame) throw new IOException("联机数据长度无效。");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        var packet = JsonSerializer.Deserialize<ExtrasPacket>(data) ?? throw new IOException("联机数据无效。");
        if (packet.Kind is null || packet.Id is null || packet.Text is null || packet.Kind.Length > 32 || packet.Id.Length > 128 || packet.Text.Length > 8192)
            throw new IOException("联机数据格式无效。");
        return packet;
    }

    private static async Task WriteAsync(Stream stream, ExtrasPacket packet, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (data.Length > MaxFrame) throw new IOException("联机数据过大。");
        var frame = System.Buffers.ArrayPool<byte>.Shared.Rent(data.Length + 4);
        try
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame, data.Length);
            data.CopyTo(frame, 4);
            // Keep the header with the first payload block; avoid a separate tiny TCP write each frame.
            for (var offset = 0; offset < data.Length + 4; offset += 8192)
                await stream.WriteAsync(frame.AsMemory(offset, Math.Min(8192, data.Length + 4 - offset)), token);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(frame, true); }
    }

    private sealed class Connection(TcpClient client, ExtrasPeer peer, IDisposable? bridge, CancellationToken token) : IDisposable
    {
        private readonly SemaphoreSlim sending = new(1, 1);
        public ExtrasPeer Peer { get; } = peer;
        public long LastSeen = Environment.TickCount64;
        public ExtrasPacket? LastScreen;
        private ExtrasPacket? lastSentScreen;
        public ConcurrentDictionary<string, CancellationTokenSource> Transfers { get; } = new();
        public async Task SendAsync(ExtrasPacket packet)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await sending.WaitAsync(timeout.Token);
            try
            {
                // Desktop Duplication reuses the exact immutable frame when the desktop has not changed.
                // Send a tiny repeat marker instead of encoding/transmitting the same JPEG 60 times a second.
                var outgoing = packet.Kind == "screen" && packet.Data is not null && packet == lastSentScreen
                    ? new ExtrasPacket("screen-repeat") : packet;
                await WriteAsync(client.GetStream(), outgoing, timeout.Token);
                if (packet.Kind == "screen") lastSentScreen = packet;
                else if (packet.Kind == "screen-stop") lastSentScreen = null;
            }
            finally { sending.Release(); }
        }
        public void Dispose() { client.Dispose(); bridge?.Dispose(); }
    }

    private sealed class Download : IDisposable
    {
        private readonly string stage, destination;
        private readonly FileStream stream;
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly long expected;
        public string Peer { get; }
        public string Id { get; }
        public Download(ExtrasFile offer, string folder)
        {
            Peer = offer.PeerIp; Id = offer.Id; expected = offer.Size;
            var safe = Path.GetFileName(offer.Name);
            foreach (var invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
            safe = safe.TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(safe)) safe = "文件";
            // A unique prefix also makes DOS device names harmless and never overwrites existing downloads.
            destination = Path.Combine(folder, Guid.NewGuid().ToString("N")[..8] + "-" + safe);
            stage = destination + ".partial";
            stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        }
        public async Task WriteAsync(ExtrasPacket packet, CancellationToken token)
        {
            if (packet.Offset != stream.Length || packet.Data is null || packet.Data.Length > 65536 || stream.Length + packet.Data.Length > expected)
                throw new IOException("文件长度不匹配。");
            await stream.WriteAsync(packet.Data, token);
            hash.AppendData(packet.Data);
        }
        public string Complete(string digest)
        {
            if (stream.Length != expected || !Convert.ToHexString(hash.GetHashAndReset()).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("文件校验失败。");
            stream.Dispose();
            File.Move(stage, destination);
            return destination;
        }
        public void Dispose() { stream.Dispose(); hash.Dispose(); if (File.Exists(stage)) File.Delete(stage); }
    }

    public void Dispose()
    {
        if (stop.IsCancellationRequested) return;
        stop.Cancel(); listener.Stop();
        foreach (var connection in connections.Values) connection.Dispose();
        connections.Clear();
        foreach (var download in downloads.Values) download.Dispose();
        downloads.Clear(); offers.Clear(); shared.Clear();
        CryptographicOperations.ZeroMemory(key);
    }
}
