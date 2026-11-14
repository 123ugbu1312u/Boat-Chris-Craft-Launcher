using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly string entranceAnimation = "Multiplayer native entrance " + ModBase.GetUuid();
    private double entranceProgress = 1;
    private (int Width, int Height, int Offset) lastPosition;
    private Rect bounds;
    private bool workspaceActive;
    private bool frameReady;
    private readonly TaskCompletionSource firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<string?>? StatusChanged;

    public MCTierWindowHost()
    {
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
            (_, _) => Attach(), Dispatcher);
        timer.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (attached != 0)
            {
                if (IsVisible && workspaceActive && frameReady) ShowWithEntrance();
                else
                {
                    ModAnimation.AniStop(entranceAnimation);
                    Native.RemoveProp(attached, "BCCL_WORKSPACE_ACTIVE");
                    PositionContainer();
                }
            }
        };
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        // STATIC paints its caption before MCTier attaches; diagnostic text here
        // otherwise flashes as visible English text on the first page opening.
        container = Native.CreateWindowEx(0, "static", string.Empty,
            0x50000000 | 0x02000000, -32000, -32000, 1, 1, parent.Handle, 0, 0, 0);
        if (container == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        started = DateTime.UtcNow;
        try
        {
            timer.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Attach));
        }
        catch (Exception error) { StatusChanged?.Invoke(error.Message); }
        return new HandleRef(this, container);
    }

    private void Attach()
    {
        if (container == 0) return;
        // Start/extraction is shared by the launcher's preload operation.
        // Never attach the initial 1x1/unthemed native window.
        if (!Current.Ready)
        {
            if (attached != 0 && !Current.Running)
            {
                frameReady = false;
                PositionContainer();
                StatusChanged?.Invoke("联机组件已关闭，点击重新打开即可继续使用。");
                timer.Stop();
            }
            return;
        }
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
            PositionContainer();
            // A hidden WebView commits React but can defer its first actual
            // paint. Keep this child shown outside the client area at its final
            // size, so layout/painting finishes before the user enters the page.
            Native.ShowWindow(attached, 4);
            _ = FinishFirstFrameAsync(window);
        }
        Fit();
    }

    internal Task PrepareFrameAsync() => firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(45));

    private async Task FinishFirstFrameAsync(nint window)
    {
        // Wait for a nonzero measured viewport, then allow WebView2 to paint.
        while (attached == window && container != 0 && (bounds.Width < 2 || bounds.Height < 2))
            await Task.Delay(16);
        await Task.Delay(240);
        if (attached != window || container == 0) return;
        frameReady = true;
        ModBase.Log($"[Multiplayer] 原生界面已预绘制：{bounds.Width:0}x{bounds.Height:0}", ModBase.LogLevel.Debug);
        firstFrame.TrySetResult();
        StatusChanged?.Invoke(null);
        if (workspaceActive) ShowWithEntrance();
    }

    internal void SetWorkspaceActive(bool value)
    {
        if (workspaceActive == value) return;
        workspaceActive = value;
        if (value && frameReady && attached != 0) ShowWithEntrance();
        else
        {
            ModAnimation.AniStop(entranceAnimation);
            entranceProgress = 1;
            if (attached != 0) Native.RemoveProp(attached, "BCCL_WORKSPACE_ACTIVE");
            PositionContainer();
            Fit();
        }
    }

    private void PositionContainer()
    {
        if (container == 0) return;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        var visible = workspaceActive && frameReady && IsVisible;
        Native.SetWindowPos(container, 0, visible ? (int)bounds.X : -width - 16,
            visible ? (int)bounds.Y : -height - 16, width, height, 0x14);
    }

    private void Fit(bool frameChanged = false)
    {
        if (attached == 0 || !Native.GetClientRect(container, out var rect)) return;
        var offset = (int)Math.Round(MCTierWindowTransition.DistanceDip * (1 - entranceProgress) * VisualTreeHelper.GetDpi(this).DpiScaleY);
        var position = (Width: Math.Max(1, rect.Right), Height: Math.Max(1, rect.Bottom), Offset: offset);
        // Mini-window commands can resize the child without changing the WPF
        // viewport. The cached target alone cannot prove that it still fits.
        if (!frameChanged && position == lastPosition && Native.GetWindowRect(attached, out var actual))
        {
            Native.MapWindowPoints(0, container, ref actual, 2);
            if (actual.Left == 0 && actual.Top == offset &&
                actual.Right == position.Width && actual.Bottom == offset + position.Height) return;
        }
        if (Native.SetWindowPos(attached, 0, 0, offset, position.Width, position.Height, frameChanged ? 0x34u : 0x14u))
            lastPosition = position;
    }

    private void ShowWithEntrance()
    {
        ModAnimation.AniStop(entranceAnimation);
        var animation = MCTierWindowTransition.Create(progress =>
        {
            entranceProgress = progress;
            Fit();
        });
        Native.SetProp(attached, "BCCL_WORKSPACE_ACTIVE", 1);
        PositionContainer();
        Native.ShowWindow(attached, 4);
        ModBase.Log($"[Multiplayer] 显示预绘制联机界面：({bounds.X:0},{bounds.Y:0}) {bounds.Width:0}x{bounds.Height:0}", ModBase.LogLevel.Debug);
        ModAnimation.AniStart(animation, entranceAnimation);
    }

    protected override void OnWindowPositionChanged(Rect bounds)
    {
        // This surface never follows the animated page's margins/transforms.
        // Its permanent parent is the full workspace viewport.
        this.bounds = bounds;
        PositionContainer();
        Fit();
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // HwndHost's default WM_WINDOWPOSCHANGING rewrites every SetWindowPos
        // back to its WPF layout slot. Our permanent host explicitly owns both
        // its parked and active rectangles in PositionContainer instead.
        if (msg == 0x46) { handled = true; return 0; }
        // The native container must never paint the system's stock white/black surface.
        if (msg == 0x14) { PaintBackground(wParam); handled = true; return 1; }
        if (msg == 0xF)
        {
            var dc = Native.BeginPaint(hwnd, out var paint);
            try { PaintBackground(dc); }
            finally { Native.EndPaint(hwnd, ref paint); }
            handled = true;
            return 0;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void PaintBackground(nint dc)
    {
        if (dc == 0 || container == 0 || !Native.GetClientRect(container, out var rect)) return;
        var color = MCTierAppearance.BackgroundColor;
        var brush = Native.CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16));
        try { Native.FillRect(dc, ref rect, brush); }
        finally { Native.DeleteObject(brush); }
    }

    protected override void DestroyWindowCore(HandleRef window)
    {
        ModAnimation.AniStop(entranceAnimation);
        entranceProgress = 1;
        lastPosition = default;
        timer.Stop();
        firstFrame.TrySetCanceled();
        if (attached != 0 && Native.IsWindow(attached))
        {
            // Never let destruction of the WPF page destroy the room process's main window.
            Native.ShowWindow(attached, 0);
            Native.RemoveProp(attached, "BCCL_WORKSPACE_ACTIVE");
            Native.SetParent(attached, 0);
            var style = Native.GetWindowLong(attached, -16).ToInt64();
            Native.SetWindowLong(attached, -16, (nint)((style & ~0x40000000L) | 0x80000000L));
        }
        attached = 0;
        Native.DestroyWindow(window.Handle);
        container = 0;
    }
}
