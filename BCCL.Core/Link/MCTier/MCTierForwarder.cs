using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BCCL.Core.Link.MCTier;

/// <summary>A loopback-only Minecraft TCP bridge into EasyTier's loopback SOCKS portal.</summary>
public sealed class MCTierForwarder : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly int socksPort, remotePort;
    private readonly IPAddress remoteIp;
    public int LocalPort { get; }

    public MCTierForwarder(int socksPort, string remoteIp, int remotePort)
    {
        if (!IPAddress.TryParse(remoteIp, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any))
            throw new ArgumentException("请填写成员列表中的虚拟 IPv4 地址。");
        if (remotePort is < 1 or > 65535) throw new ArgumentException("游戏端口应为 1–65535。");
        this.socksPort = socksPort;
        this.remoteIp = ip;
        this.remotePort = remotePort;
        listener.Start();
        LocalPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                _ = RelayAsync(client);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var upstream = new TcpClient())
        using (var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            try
            {
                client.NoDelay = true;
                upstream.NoDelay = true;
                session.CancelAfter(TimeSpan.FromSeconds(10));
                await upstream.ConnectAsync(IPAddress.Loopback, socksPort, session.Token);
                using var proxy = upstream.GetStream();
                using var local = client.GetStream();
                await proxy.WriteAsync(new byte[] { 5, 1, 0 }, session.Token);
                var hello = new byte[2];
                await proxy.ReadExactlyAsync(hello, session.Token);
                if (hello[0] != 5 || hello[1] != 0) return;
                var request = new byte[10] { 5, 1, 0, 1, 0, 0, 0, 0, (byte)(remotePort >> 8), (byte)remotePort };
                remoteIp.GetAddressBytes().CopyTo(request, 4);
                await proxy.WriteAsync(request, session.Token);
                var reply = new byte[4];
                await proxy.ReadExactlyAsync(reply, session.Token);
                if (reply[0] != 5 || reply[1] != 0) return;
                var length = reply[3] switch { 1 => 4, 4 => 16, 3 => await ReadByteAsync(proxy, session.Token), _ => throw new IOException("SOCKS response invalid") };
                await proxy.ReadExactlyAsync(new byte[length + 2], session.Token);
                session.CancelAfter(Timeout.InfiniteTimeSpan);
                var sending = local.CopyToAsync(proxy, session.Token);
                var receiving = proxy.CopyToAsync(local, session.Token);
                await Task.WhenAny(sending, receiving);
                session.Cancel();
                try { await Task.WhenAll(sending, receiving); } catch (OperationCanceledException) { }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken token)
    {
        var value = new byte[1];
        await stream.ReadExactlyAsync(value, token);
        return value[0];
    }

    public void Dispose()
    {
        if (stop.IsCancellationRequested) return;
        stop.Cancel();
        listener.Stop();
    }
}
