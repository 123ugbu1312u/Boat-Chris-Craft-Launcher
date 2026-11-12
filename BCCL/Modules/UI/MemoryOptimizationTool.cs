using System.ComponentModel;
using System.Diagnostics;
using BCCL.Core.App;
using BCCL.Core.App.Localization;
using BCCL.Core.UI;
using BCCL.Core.Utils.OS;

namespace BCCL;

internal static class MemoryOptimizationTool
{
    private static int _running;

    public static async Task RunAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            HintService.Hint(Lang.Text("Tools.Test.Memory.Busy"));
            return;
        }
        try
        {
            HintService.Hint(Lang.Text("Tools.Test.Memory.Running"));
            var result = await Task.Run(async () =>
            {
                if (ProcessInterop.IsAdmin()) return MemoryOptimizer.Run();
                var before = KernelInterop.GetAvailablePhysicalMemoryBytes();
                using var child = Process.Start(new ProcessStartInfo(Basics.ExecutablePath)
                {
                    Arguments = "--memory", UseShellExecute = true, Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true,
                    WorkingDirectory = Basics.ExecutableDirectory
                }) ?? throw new InvalidOperationException("Cannot start memory optimization helper.");
                await child.WaitForExitAsync().ConfigureAwait(false);
                if (child.ExitCode < 0) throw new InvalidOperationException($"Memory optimization helper failed ({child.ExitCode}).");
                var after = KernelInterop.GetAvailablePhysicalMemoryBytes();
                // Child exit code reports KiB. Measure the current available memory independently for display.
                return new MemoryOptimizationResult(before, after);
            });
            ModBase.Log($"[Memory] 内存优化完成：可用内存改变量 {result.AvailableChange} bytes，当前可用 {result.AvailableAfter} bytes");
            if (result.AvailableChange > 0)
                HintService.Hint(Lang.Text("Tools.Test.Memory.Finished", FormatBytes((ulong)result.AvailableChange), FormatBytes(result.AvailableAfter)), HintType.Success);
            else
                HintService.Hint(Lang.Text("Tools.Test.Memory.NoChange", FormatBytes(result.AvailableAfter)));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            HintService.Hint(Lang.Text("Tools.Test.Memory.Cancelled"));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Memory] 内存优化失败");
            HintService.Hint(Lang.Text("Tools.Test.Memory.Failed"), HintType.Error);
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private static string FormatBytes(ulong bytes) => bytes >= 1024UL * 1024 * 1024
        ? Lang.Number(bytes / (1024d * 1024 * 1024), "N2") + " GiB"
        : Lang.Number(bytes / (1024d * 1024), "N1") + " MiB";
}
