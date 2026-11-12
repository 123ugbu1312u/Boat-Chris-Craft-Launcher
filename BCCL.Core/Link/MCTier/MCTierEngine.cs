using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BCCL.Core.Link.MCTier;

public sealed record MCTierStartRequest(MCTierRoom Room, string Nickname, bool VirtualAdapter, int RpcPort, int SocksPort, int GamePort, bool ManagedRoom = false, string? VirtualIp = null, bool RequireAdministrator = false);
public sealed record MCTierPeer(string Name, string Ip, string Connection, string Latency, bool IsLocal)
{
    public string Display => $"{Name} · {Ip} · {Connection} · {Latency} ms";
}

public static class MCTierEngine
{
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    public static Process Create(MCTierStartRequest request)
    {
        request.Room.Validate();
        if (request.Nickname.Length is < 1 or > 32 || request.Nickname.Any(char.IsControl) || request.Nickname.Contains('|'))
            throw new ArgumentException("玩家昵称应为 1–32 个字符，不能包含竖线或控制字符。");
        if (request.RpcPort is < 1 or > 65535 || request.SocksPort is < 1 or > 65535 || request.GamePort is < 1 or > 65535)
            throw new ArgumentException("联机端口无效。");
        var start = new ProcessStartInfo(Path.Combine(MCTierRuntime.DirectoryPath, "easytier-core.exe"))
        {
            WorkingDirectory = MCTierRuntime.DirectoryPath, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        void Add(string key, string value) { start.ArgumentList.Add("--" + key); start.ArgumentList.Add(value); }
        // Keep secrets out of command lines and diagnostic output.
        start.Environment["ET_NETWORK_SECRET"] = request.Room.NetworkPassword;
        var networkName = request.ManagedRoom ? "MCTier-" + request.Room.Name : request.Room.Name;
        Add("network-name", networkName);
        Add("hostname", request.Nickname);
        Add("instance-name", "BCCL-MCTier-" + Guid.NewGuid().ToString("N"));
        Add("peers", request.Room.Node);
        Add("rpc-portal", $"127.0.0.1:{request.RpcPort}");
        Add("listeners", "udp://0.0.0.0:0");
        if (request.VirtualIp is not null)
        {
            if (!request.ManagedRoom || !IPAddress.TryParse(request.VirtualIp, out var fixedIp) ||
                fixedIp.AddressFamily != AddressFamily.InterNetwork || !request.VirtualIp.StartsWith("10.126.126.", StringComparison.Ordinal) || fixedIp.GetAddressBytes()[3] is 0 or 255)
                throw new ArgumentException("房间保留的虚拟地址无效。");
            Add("dhcp", "false");
            Add("ipv4", request.VirtualIp);
        }
        else Add("dhcp", "true");
        Add("encryption-algorithm", "aes-256-gcm");
        Add("private-mode", "true");
        Add("relay-network-whitelist", networkName);
        Add("console-log-level", "error");
        Add("file-log-level", "off");
        if (request.VirtualAdapter) Add("dev-name", "BCCL-MCTier");
        else
        {
            Add("no-tun", "true");
            Add("use-smoltcp", "true");
            Add("tcp-whitelist", request.GamePort.ToString());
            start.ArgumentList.Add(MCTierExtrasHub.Port.ToString());
            Add("udp-whitelist", request.GamePort.ToString());
        }
        var config = Path.Combine(MCTierRuntime.DirectoryPath, $"session-{request.RpcPort}.toml");
        File.WriteAllText(config, $"socks5_proxy = \"socks5://127.0.0.1:{request.SocksPort}\"\n");
        Add("config-file", config);
        return new Process { StartInfo = start, EnableRaisingEvents = true };
    }

    public static async Task<IReadOnlyList<MCTierPeer>> ReadPeersAsync(int port, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(MCTierRuntime.DirectoryPath, "easytier-cli.exe"))
        {
            WorkingDirectory = MCTierRuntime.DirectoryPath, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "--rpc-portal", $"127.0.0.1:{port}", "-o", "json", "peer" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法查询联机核心。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            await errorTask;
            if (process.ExitCode != 0) throw new IOException("联机核心正在准备，稍后自动重试。");
            return ParsePeers(output);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    public static IReadOnlyList<MCTierPeer> ParsePeers(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var peers = new List<MCTierPeer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new IOException("成员列表格式无效。");
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string Get(string key) => item.TryGetProperty(key, out var value) ? value.ToString() : "";
            var ip = Get("ipv4").Split('/')[0];
            if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork) continue;
            var cost = Get("cost");
            var local = cost.Contains("local", StringComparison.OrdinalIgnoreCase);
            peers.Add(new MCTierPeer(Get("hostname"), ip, local ? "本机" : cost.Contains("p2p", StringComparison.OrdinalIgnoreCase) ? "直连" : "中继", Get("lat_ms"), local));
        }
        return peers;
    }
}
