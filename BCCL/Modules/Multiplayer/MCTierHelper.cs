using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Security.Principal;
using BCCL.Core.Link.MCTier;

namespace BCCL.Multiplayer;

/// <summary>Owns only this session's engine. Pipe disconnect also cleans up after a launcher crash.</summary>
internal static class MCTierHelper
{
    public static async Task RunAsync(string pipeName, int parentId, long parentStart)
    {
        using var parent = Process.GetProcessById(parentId);
        if (parent.StartTime.ToUniversalTime().Ticks != parentStart || !pipeName.StartsWith("BCCL-MCTier-", StringComparison.Ordinal)) return;
        using var pipe = MCTierControlPipe.CreateClient(pipeName);
        await pipe.ConnectAsync(15000);
        MCTierControlPipe.VerifyServer(pipe, parentId);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var json = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("会话已关闭。");
        if (json.Length > 16000) return;
        var request = JsonSerializer.Deserialize<MCTierStartRequest>(json) ?? throw new IOException("会话无效。");
        if (request.RequireAdministrator || request.VirtualAdapter)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("此联机会话必须使用管理员权限。");
        }
        using var engine = MCTierEngine.Create(request);
        try
        {
            engine.Start();
            // Drain continuously; never retain network names or secrets in log files.
            engine.OutputDataReceived += (_, _) => { };
            engine.ErrorDataReceived += (_, _) => { };
            engine.BeginOutputReadLine();
            engine.BeginErrorReadLine();
            await writer.WriteLineAsync("READY");
            using var watch = new CancellationTokenSource();
            var command = reader.ReadLineAsync(watch.Token).AsTask();
            var exited = engine.WaitForExitAsync(watch.Token);
            var parentExited = parent.WaitForExitAsync(watch.Token);
            var completed = await Task.WhenAny(command, exited, parentExited);
            if (completed == exited)
                await writer.WriteLineAsync("EXIT:" + engine.ExitCode);
            watch.Cancel();
            try { await Task.WhenAll(command, exited, parentExited); } catch (OperationCanceledException) { }
        }
        finally
        {
            try { if (!engine.HasExited) { engine.Kill(true); await engine.WaitForExitAsync(); } } catch (InvalidOperationException) { }
            var config = Path.Combine(MCTierRuntime.DirectoryPath, $"session-{request.RpcPort}.toml");
            if (File.Exists(config)) File.Delete(config);
        }
    }
}
