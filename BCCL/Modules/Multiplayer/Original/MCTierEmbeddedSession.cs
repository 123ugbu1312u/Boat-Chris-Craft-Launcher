using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BCCL.Core.App.IoC;

namespace BCCL.Multiplayer.Original;

/// <summary>Owns only the bundled original MCTier process. Page navigation never stops a room.</summary>
internal sealed class MCTierEmbeddedSession
{
    public static MCTierEmbeddedSession Current { get; } = new();
    private Process? process;
    public nint Window { get; private set; }
    public bool Running => process is { HasExited: false };
    public string ExecutablePath => Path.Combine(AppContext.BaseDirectory,
        "Extensions", "Multiplayer", "MCTier", "MCTier.exe");

    private MCTierEmbeddedSession()
    {
        Lifecycle.When(LifecycleState.Exiting, Close);
        ThemeManager.AppearanceChanged += MCTierAppearance.Publish;
    }

    public void Start()
    {
        MCTierAppearance.Publish();
        if (Running) return;
        Window = 0;
        process?.Dispose();
        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("联机组件未随启动器一起复制，请保留 Extensions/Multiplayer/MCTier 文件夹。", ExecutablePath);
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
        [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint window, nint parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLong(nint window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLong(nint window, int index, nint value);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowEx(int exStyle, string className, string title, int style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    }
}
