using System.ComponentModel;
using System.Diagnostics;
using BCCL.Core.App;
using BCCL.Core.App.Localization;
using BCCL.Core.UI;
using BCCL.Core.Utils.OS;

namespace BCCL;

internal static class MemoryOptimizationTool
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task RunAsync()
    {
        if (!await Gate.WaitAsync(0))
        {
            HintService.Hint(Lang.Text("Tools.Test.Memory.Busy"));
            return;
        }
        try { await RunCoreAsync(CancellationToken.None, beforeLaunch: false); }
        finally { Gate.Release(); }
    }

    public static async Task RunBeforeLaunchAsync(CancellationToken cancellationToken)
    {
        // 如果工具页已经在优化，先等待它结束，避免启动与内存整理同时进行。
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RunCoreAsync(cancellationToken, beforeLaunch: true).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    private static async Task RunCoreAsync(CancellationToken cancellationToken, bool beforeLaunch)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            HintService.Hint(Lang.Text("Tools.Test.Memory.Running"));
            var result = await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ProcessInterop.IsAdmin()) return MemoryOptimizer.Run();
                var before = KernelInterop.GetAvailablePhysicalMemoryBytes();
                using var child = Process.Start(new ProcessStartInfo(Basics.ExecutablePath)
                {
                    Arguments = "--memory", UseShellExecute = true, Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true,
                    WorkingDirectory = Basics.ExecutableDirectory
                }) ?? throw new InvalidOperationException("Cannot start memory optimization helper.");
                // 系统内存整理不能安全地中途终止。取消启动后由助手自行结束，启动任务不再等待。
                await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (child.ExitCode < 0) throw new InvalidOperationException($"Memory optimization helper failed ({child.ExitCode}).");
                var after = KernelInterop.GetAvailablePhysicalMemoryBytes();
                // Child exit code reports KiB. Measure the current available memory independently for display.
                return new MemoryOptimizationResult(before, after);
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ModBase.Log($"[Memory] 内存优化完成：可用内存改变量 {result.AvailableChange} bytes，当前可用 {result.AvailableAfter} bytes");
            if (result.AvailableChange > 0)
                HintService.Hint(Lang.Text("Tools.Test.Memory.Finished", FormatBytes((ulong)result.AvailableChange), FormatBytes(result.AvailableAfter)), HintType.Success);
            else
                HintService.Hint(Lang.Text("Tools.Test.Memory.NoChange", FormatBytes(result.AvailableAfter)));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            HintService.Hint(Lang.Text(beforeLaunch ? "Minecraft.Launch.MemoryOptimize.Cancelled" : "Tools.Test.Memory.Cancelled"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Memory] 内存优化失败");
            HintService.Hint(Lang.Text(beforeLaunch ? "Minecraft.Launch.MemoryOptimize.Failed" : "Tools.Test.Memory.Failed"), HintType.Error);
        }
    }

    private static string FormatBytes(ulong bytes) => bytes >= 1024UL * 1024 * 1024
        ? Lang.Number(bytes / (1024d * 1024 * 1024), "N2") + " GiB"
        : Lang.Number(bytes / (1024d * 1024), "N1") + " MiB";
}
