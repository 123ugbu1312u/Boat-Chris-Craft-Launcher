using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BCCL.Core.App.IoC;
using BCCL.Core.App;

namespace BCCL.Multiplayer.Original;

/// <summary>Owns only the bundled original MCTier process. Page navigation never stops a room.</summary>
internal sealed class MCTierEmbeddedSession
{
    public static MCTierEmbeddedSession Current { get; } = new();
    private Process? process;
    public nint Window { get; private set; }
    public bool Running => process is { HasExited: false };
    private readonly SemaphoreSlim preparationGate = new(1, 1);
    private string? executablePath;
    private Task? startupTask;
    private bool closing;
    private readonly CancellationTokenSource shutdown = new();
    public string ExecutablePath => executablePath ?? throw new InvalidOperationException("联机组件尚未准备完成。");
    public bool Ready => FindWindow() is var window && window != 0 && Native.GetProp(window, "BCCL_WORKSPACE_READY") == 1;

    public async Task PreloadAsync()
    {
        try
        {
            ModBase.Log("[Multiplayer] 开始后台预加载联机界面", ModBase.LogLevel.Debug);
            await EnsureReadyAsync();
            ModBase.Log("[Multiplayer] 后台联机界面已就绪", ModBase.LogLevel.Debug);
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { ModBase.Log(error, "[Multiplayer] 后台预加载未完成，可在联机页重试", ModBase.LogLevel.Debug); }
    }

    // UI-thread callers share one startup operation, including an early click during preloading.
    public Task EnsureReadyAsync(IProgress<string>? progress = null)
    {
        if (closing) return Task.FromCanceled(shutdown.Token);
        if (Ready) return Task.CompletedTask;
        if (startupTask == null || startupTask.IsCompleted) startupTask = StartAndWaitAsync(progress);
        return startupTask;
    }

    private async Task StartAndWaitAsync(IProgress<string>? progress)
    {
        await PrepareAsync(progress);
        shutdown.Token.ThrowIfCancellationRequested();
        Start();
        progress?.Report("正在后台加载联机界面，请稍候…");
        var waiting = Stopwatch.StartNew();
        while (!Ready)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            if (!Running) throw new IOException("联机组件启动失败，请重新打开重试。");
            if (waiting.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException("联机界面仍未就绪，请稍后重试。");
            await Task.Delay(60, shutdown.Token);
        }
        // Let the committed theme and layout settle while the native window remains hidden.
        await Task.Delay(120, shutdown.Token);
    }

    public async Task PrepareAsync(IProgress<string>? progress = null)
    {
        await preparationGate.WaitAsync();
        try
        {
            if (Running) return;
            var cache = Path.Combine(Paths.SharedLocalData, "Multiplayer", "Runtime");
            executablePath = await Task.Run(() => MCTierPayload.EnsureAvailable(cache, progress));
        }
        finally { preparationGate.Release(); }
    }

    private MCTierEmbeddedSession()
    {
        Lifecycle.When(LifecycleState.Exiting, Close);
        ThemeManager.AppearanceChanged += MCTierAppearance.Publish;
    }

    public void Start()
    {
        if (closing) throw new OperationCanceledException(shutdown.Token);
        MCTierAppearance.Publish();
        if (Running) return;
        Window = 0;
        process?.Dispose();
        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("联机组件准备失败，请点击重新打开重试。", ExecutablePath);
        var start = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
        };
        start.Environment["BCCL_MCTIER_EMBEDDED"] = "1";
        start.Environment["BCCL_MCTIER_PARENT_PID"] = Environment.ProcessId.ToString();
        start.Environment["BCCL_MCTIER_APPEARANCE_FILE"] = MCTierAppearance.FilePath;
        process = Process.Start(start) ?? throw new IOException("联机组件未能启动。");
    }

    public nint FindWindow()
    {
        if (!Running) { Window = 0; return 0; }
        if (Window != 0 && Native.IsWindow(Window)) return Window;
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            if (pid != process!.Id) return true;
            var title = new System.Text.StringBuilder(256);
            Native.GetWindowText(window, title, title.Capacity);
            var caption = title.ToString();
            if (caption != "BCCL虚拟局域网联机" && !caption.StartsWith("MCTier", StringComparison.Ordinal)) return true;
            Window = window;
            return false;
        }, 0);
        return Window;
    }

    public void Close()
    {
        closing = true;
        shutdown.Cancel();
        if (FindWindow() is var window && window != 0)
            Native.PostMessage(window, 0x10, 0, 0); // Original MCTier close handler performs cleanup.
        // A native parent-process watcher also cleans up if the launcher crashes.
    }

    internal static class Native
    {
        internal delegate bool EnumWindowsCallback(nint window, nint parameter);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out int processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
        [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)] internal static extern nint GetProp(nint window, string name);
        [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode)] internal static extern bool SetProp(nint window, string name, nint value);
        [DllImport("user32.dll", EntryPoint = "RemovePropW", CharSet = CharSet.Unicode)] internal static extern nint RemoveProp(nint window, string name);
        [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint window, nint parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLong(nint window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLong(nint window, int index, nint value);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern int MapWindowPoints(nint from, nint to, ref Rect rect, uint count);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowEx(int exStyle, string className, string title, int style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
        [DllImport("user32.dll")] internal static extern nint BeginPaint(nint window, out PaintStruct paint);
        [DllImport("user32.dll")] internal static extern bool EndPaint(nint window, ref PaintStruct paint);
        [DllImport("user32.dll")] internal static extern int FillRect(nint dc, ref Rect rect, nint brush);
        [DllImport("gdi32.dll")] internal static extern nint CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct PaintStruct
        {
            public nint Dc;
            public int Erase;
            public Rect Rect;
            public int Restore, IncrementalUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
        }
    }
}
