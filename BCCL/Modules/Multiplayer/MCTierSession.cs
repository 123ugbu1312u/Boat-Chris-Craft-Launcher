using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using BCCL.Core.App;
using BCCL.Core.App.IoC;
using BCCL.Core.Link.MCTier;

namespace BCCL.Multiplayer;

internal sealed class MCTierSession
{
    public static MCTierSession Current { get; } = new();
    private NamedPipeServerStream? pipe;
    private StreamWriter? writer;
    private Process? helper;
    private CancellationTokenSource? lifetime;
    private readonly SemaphoreSlim gate = new(1, 1);
    private MCTierForwarder? forwarder;
    private MCTierDirectoryLease? directoryLease;
    private TaskCompletionSource<string>? addressReady;
    public MCTierStartRequest? Request { get; private set; }
    public MCTierExtrasHub? Extras { get; private set; }
    public string ExtrasError { get; private set; } = "";
    public bool Running { get; private set; }
    public string Status { get; private set; } = "尚未连接";
    public IReadOnlyList<MCTierPeer> Peers { get; private set; } = [];
    public string LocalAddress => Peers.FirstOrDefault(p => p.IsLocal)?.Ip ?? "等待分配";
    public string GameAddress { get; private set; } = "";
    public event Action? Changed;

    private MCTierSession()
    {
        Lifecycle.When(LifecycleState.Exiting, ClosePipe);
    }

    public async Task StartAsync(MCTierRoom room, string nickname, bool adapter, int gamePort, CancellationToken token)
        => await StartCoreAsync(room, nickname, adapter, gamePort, false, token);

    public async Task CreateAsync(MCTierRoom room, string nickname, bool adapter, int gamePort, CancellationToken token)
        => await StartCoreAsync(room, nickname, adapter, gamePort, false, token, requireAdministrator: true);

    public async Task StartRegisteredAsync(MCTierRoom room, string nickname, bool adapter, int gamePort,
        MCTierDirectoryLease? reservation, bool create, Uri endpoint, CancellationToken token)
    {
        MCTierDirectoryLease? admitted = reservation;
        try
        {
            admitted ??= create
                ? await MCTierDirectoryLease.ReserveAsync(room, nickname, endpoint, token)
                : await MCTierDirectoryLease.JoinAllocatedAsync(room, nickname, endpoint, token);
            if (!admitted.IsAlive || admitted.Room != room || admitted.Endpoint != endpoint)
                throw new IOException("房间保留已失效，请重新创建或加入。");
            // Start with the address already reserved by the directory; no DHCP wait or second TLS handshake.
            await StartCoreAsync(room, nickname, adapter, gamePort, true, token, admitted.VirtualIp, requireAdministrator: create);
            SetStatus("正在启动房间网络…");
            await addressReady!.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            if (!admitted.IsAlive) throw new IOException("房间名称服务已断开，请重新连接。");
            directoryLease = admitted;
            admitted = null;
            var ownedLease = directoryLease;
            _ = WatchDirectoryAsync(ownedLease);
            SetStatus(Peers.Any(p => !p.IsLocal) ? "虚拟网络就绪，已发现其他成员" : "房间已就绪，等待朋友加入");
        }
        catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            await StopAsync();
            throw new IOException("房间网络启动超时，请检查联机组件后重试。", ex);
        }
        catch
        {
            await StopAsync();
            throw;
        }
        finally { if (admitted is not null) await admitted.DisposeAsync(); }
    }

    private async Task WatchDirectoryAsync(MCTierDirectoryLease owned)
    {
        await owned.Completion;
        if (!ReferenceEquals(directoryLease, owned)) return;
        await StopAsync();
        SetStatus("房间名称服务连接已断开，已退出大厅。请重新连接。");
    }

    private async Task StartCoreAsync(MCTierRoom room, string nickname, bool adapter, int gamePort, bool managedRoom, CancellationToken token, string? virtualIp = null, bool requireAdministrator = false)
    {
        await gate.WaitAsync(token);
        try
        {
            if (Running || helper is not null) throw new InvalidOperationException("请先断开当前大厅。");
            room.Validate();
            if (gamePort == MCTierExtrasHub.Port) throw new ArgumentException($"端口 {MCTierExtrasHub.Port} 用于大厅其他功能，请为游戏选择其他端口。");
            await MCTierRuntime.EnsureAsync(SetStatus, token);
            var rpc = MCTierEngine.FreePort();
            var socks = MCTierEngine.FreePort();
            while (socks == rpc) socks = MCTierEngine.FreePort();
            Request = new MCTierStartRequest(room, nickname, adapter, rpc, socks, gamePort, managedRoom, virtualIp, requireAdministrator || adapter);
            addressReady = managedRoom ? new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            // Validate before showing UAC or spawning any process.
            using (var validation = MCTierEngine.Create(Request)) { }
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pipeName = "BCCL-MCTier-" + Guid.NewGuid().ToString("N");
            pipe = MCTierControlPipe.CreateServer(pipeName);
            using var parent = Process.GetCurrentProcess();
            var start = MCTierHelperLaunch.Create(Environment.ProcessPath!, Basics.ExecutableDirectory, pipeName,
                parent.Id, parent.StartTime.ToUniversalTime().Ticks, requireAdministrator, adapter);
            SetStatus(requireAdministrator ? "创建房间需要管理员权限，请完成 Windows 授权…" : adapter ? "正在准备虚拟网卡，请完成 Windows 权限确认…" : "正在启动 MC 联机…");
            // ShellExecute may wait for UAC. Keep the UI and reserved-room heartbeat responsive.
            helper = await Task.Run(() => Process.Start(start)) ?? throw new IOException("无法启动联机核心。");
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            await pipe.WaitForConnectionAsync(connectTimeout.Token);
            MCTierControlPipe.VerifyClient(pipe, helper.Id);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            // The helper needs only the compatible network credential, never the original long password.
            await writer.WriteLineAsync(JsonSerializer.Serialize(Request with { Room = room with { Password = room.NetworkPassword } }));
            if (await reader.ReadLineAsync(connectTimeout.Token) != "READY") throw new IOException("联机核心未能启动。");
            Running = true;
            ExtrasError = "";
            try { Extras = new MCTierExtrasHub(room, nickname, adapter ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback); }
            catch (System.Net.Sockets.SocketException) { ExtrasError = $"其他功能端口 {MCTierExtrasHub.Port} 被占用。游戏联机仍可使用；释放端口后重新加入大厅可重试。"; }
            SetStatus(virtualIp is null ? "核心运行中，正在分配虚拟地址…" : "正在启动房间网络…");
            _ = WatchHelperAsync(reader, lifetime.Token);
            _ = RefreshLoopAsync(lifetime.Token);
        }
        catch (Exception ex)
        {
            ClosePipe();
            SetStatus("尚未连接");
            if (ex is Win32Exception { NativeErrorCode: 1223 }) throw new InvalidOperationException(requireAdministrator
                ? "已取消管理员授权，未创建房间。创建房间必须完成 Windows 授权。"
                : "已取消系统授权，可以选择无需管理员的方式加入房间。", ex);
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task WatchHelperAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            var result = await reader.ReadLineAsync(token);
            if (!token.IsCancellationRequested)
            {
                var reason = result?.StartsWith("EXIT:", StringComparison.Ordinal) == true ? $"（退出码 {result[5..]}）" : "";
                ClosePipe();
                SetStatus("联机核心已退出" + reason + "。请检查组件、端口占用或系统权限后重新连接。");
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested)
            {
                ClosePipe();
                SetStatus("与联机核心的连接已中断，请重新连接大厅。");
            }
        }
        finally { reader.Dispose(); }
    }

    private async Task RefreshLoopAsync(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var peers = await MCTierEngine.ReadPeersAsync(Request!.RpcPort, token);
                if (token.IsCancellationRequested) return;
                Peers = peers;
                var localIp = peers.FirstOrDefault(p => p.IsLocal)?.Ip;
                if (localIp is not null && (Request!.VirtualIp is null || Request.VirtualIp == localIp)) addressReady?.TrySetResult(localIp);
                Extras?.Discover(peers, Request!.SocksPort);
                failures = 0;
                var remote = peers.Count(p => !p.IsLocal);
                SetStatus(peers.Any(p => p.IsLocal) ? remote > 0 ? $"虚拟网络就绪 · {remote} 位远端成员" : "虚拟网络就绪，等待其他玩家加入" : "核心运行中，等待分配虚拟地址…");
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or Win32Exception)
            {
                if (token.IsCancellationRequested) return;
                if (++failures >= 3) SetStatus("暂时无法读取成员状态，正在重试。请检查节点地址与网络。");
            }
            try { await Task.Delay(Peers.Any(p => p.IsLocal) ? 3000 : 150, token); } catch (OperationCanceledException) { return; }
        }
    }

    public void Forward(string ip, int port)
    {
        if (!Running || Request is null) throw new InvalidOperationException("请先连接大厅。");
        var next = new MCTierForwarder(Request.SocksPort, ip, port);
        forwarder?.Dispose();
        forwarder = next;
        GameAddress = $"127.0.0.1:{next.LocalPort}";
        Changed?.Invoke();
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try
        {
            var process = helper;
            ClosePipe();
            if (process is not null)
            {
                using (process)
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { SetStatus("正在等待联机核心清理会话…"); return; }
            }
            SetStatus("已断开联机");
        }
        finally { gate.Release(); }
    }

    private void ClosePipe()
    {
        addressReady?.TrySetCanceled();
        Extras?.Dispose();
        Extras = null;
        ExtrasError = "";
        var oldLease = directoryLease;
        directoryLease = null;
        oldLease?.Dispose();
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        pipe?.Dispose();
        pipe = null;
        writer = null;
        helper = null;
        forwarder?.Dispose();
        forwarder = null;
        Running = false;
        Peers = [];
        GameAddress = "";
        Changed?.Invoke();
    }

    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }
}
