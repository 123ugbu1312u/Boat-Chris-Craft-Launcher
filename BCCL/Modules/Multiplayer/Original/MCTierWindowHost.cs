using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static BCCL.Multiplayer.Original.MCTierEmbeddedSession;

namespace BCCL.Multiplayer.Original;

/// <summary>Hosts MCTier's own WebView window; no launcher recreation of its UI or protocols.</summary>
internal sealed class MCTierWindowHost : HwndHost
{
    private readonly DispatcherTimer timer;
    private nint container;
    private nint attached;
    private DateTime started;
    public event Action<string?>? StatusChanged;

    public MCTierWindowHost()
    {
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
            (_, _) => Attach(), Dispatcher);
        timer.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (attached != 0) Native.ShowWindow(attached, IsVisible ? 5 : 0);
        };
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        container = Native.CreateWindowEx(0, "static", "BCCL MCTier host",
            0x50000000 | 0x02000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (container == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        started = DateTime.UtcNow;
        try { Current.Start(); timer.Start(); }
        catch (Exception error) { StatusChanged?.Invoke(error.Message); }
        return new HandleRef(this, container);
    }

    private void Attach()
    {
        if (container == 0) return;
        var window = Current.FindWindow();
        if (window == 0)
        {
            if (!Current.Running) { StatusChanged?.Invoke("联机组件已关闭，点击重新打开即可继续使用。"); timer.Stop(); }
            else if ((DateTime.UtcNow - started).TotalSeconds > 40)
                StatusChanged?.Invoke("联机组件仍在启动，请稍候。日志位于 LocalAppData/com.bccl.mctier.embedded/mctier.log。");
            return;
        }
        if (attached != window)
        {
            Native.ShowWindow(window, 0);
            var style = Native.GetWindowLong(window, -16).ToInt64();
            Native.SetWindowLong(window, -16, (nint)((style & ~0x80CF0000L) | 0x40000000L));
            var extendedStyle = Native.GetWindowLong(window, -20).ToInt64();
            Native.SetWindowLong(window, -20, (nint)(extendedStyle & ~0x00040308L));
            Marshal.SetLastPInvokeError(0);
            Native.SetParent(window, container);
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                StatusChanged?.Invoke($"无法内嵌联机窗口：{new System.ComponentModel.Win32Exception(error).Message}");
                return;
            }
            attached = window;
            // The embedded surface has no independent non-client frame or DWM outline.
            var disabled = 1;
            Native.DwmSetWindowAttribute(window, 2, ref disabled, sizeof(int));
            Native.DwmSetWindowAttribute(window, 33, ref disabled, sizeof(int));
            var noBorder = unchecked((int)0xFFFFFFFE);
            Native.DwmSetWindowAttribute(window, 34, ref noBorder, sizeof(int));
            Fit(frameChanged: true);
            if (IsVisible) Native.ShowWindow(window, 5);
            StatusChanged?.Invoke(null);
        }
        Fit();
    }

    private void Fit(bool frameChanged = false)
    {
        if (attached == 0 || !Native.GetClientRect(container, out var rect)) return;
        if (!frameChanged && Native.GetClientRect(attached, out var current) && current.Right == rect.Right && current.Bottom == rect.Bottom) return;
        Native.SetWindowPos(attached, 0, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), frameChanged ? 0x34u : 0x14u);
    }

    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        Fit();
    }

    protected override void DestroyWindowCore(HandleRef window)
    {
        timer.Stop();
        if (attached != 0 && Native.IsWindow(attached))
        {
            // Never let destruction of the WPF page destroy the room process's main window.
            Native.ShowWindow(attached, 0);
            Native.SetParent(attached, 0);
            var style = Native.GetWindowLong(attached, -16).ToInt64();
            Native.SetWindowLong(attached, -16, (nint)((style & ~0x40000000L) | 0x80000000L));
        }
        attached = 0;
        Native.DestroyWindow(window.Handle);
        container = 0;
    }
}
