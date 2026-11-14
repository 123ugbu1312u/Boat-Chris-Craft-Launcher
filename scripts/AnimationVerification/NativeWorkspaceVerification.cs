using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static BCCL.ModAnimation;

// All HWNDs inspected here are created by this test. No running launcher or
// user window is inspected, and no multiplayer process/network is started.
internal static class NativeWorkspaceVerification
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var type = typeof(BCCL.FormMain).Assembly.GetType("BCCL.Multiplayer.Original.MCTierWindowHost")!;
        var host = (HwndHost)Activator.CreateInstance(type)!;
        var owner = new Window { Content = host, Left = -20000, Top = -20000, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        nint child = 0;
        try
        {
            owner.Show();
            owner.UpdateLayout();
            var parent = new WindowInteropHelper(owner).Handle;
            var container = (nint)type.GetField("container", Hidden)!.GetValue(host)!;
            if (container == 0 || !host.IsVisible) throw new Exception("Persistent host was not constructed during startup layout.");
            child = CreateWindowEx(0, "static", "Owned verification surface", 0x50000000, 0, 0, 1, 1, container, 0, 0, 0);
            if (child == 0) throw new Exception("Cannot create owned verification surface.");
            type.GetField("attached", Hidden)!.SetValue(host, child);
            var position = type.GetMethod("OnWindowPositionChanged", Hidden)!;
            var activate = type.GetMethod("SetWorkspaceActive", Hidden)!;
            var bounds = new Rect(152, 154, 900, 500);
            position.Invoke(host, [bounds]);
            AssertParked(container, parent);
            activate.Invoke(host, [true]);
            AssertParked(container, parent); // Readiness must gate first visibility.
            type.GetField("frameReady", Hidden)!.SetValue(host, true);
            activate.Invoke(host, [false]);
            activate.Invoke(host, [true]);
            AssertBounds(container, parent, bounds);
            // Layout changes during entry must track the viewport, never the
            // transient page transform or its old left column.
            bounds = new Rect(76, 154, 1100, 640);
            position.Invoke(host, [bounds]);
            AssertBounds(container, parent, bounds);
            foreach (var group in aniGroups.Values) group.startTick = 0;
            AniTimer(300);
            AssertBounds(container, parent, bounds);
            var fit = type.GetMethod("Fit", Hidden)!;
            // Reproduce the independent room UI resizing its native child while
            // the workspace and host's cached target rectangle stay unchanged.
            // The next fit must repair the actual HWND, including its origin.
            for (var cycle = 0; cycle < 6; cycle++)
            {
                foreach (var height in new[] { 50, 520 })
                {
                    SetWindowPos(child, 0, 12, 8, 320, height, 0x14);
                    fit.Invoke(host, [false]);
                    var actual = Bounds(child, container);
                    if (actual.Left != 0 || actual.Top != 0 || actual.Right != (int)bounds.Width || actual.Bottom != (int)bounds.Height)
                        throw new Exception($"Collapse/expand left stale native size: ({actual.Left},{actual.Top})-({actual.Right},{actual.Bottom}).");
                }
            }
            activate.Invoke(host, [false]);
            AssertParked(container, parent);
            activate.Invoke(host, [true]);
            AssertBounds(container, parent, bounds);
            if ((nint)type.GetField("container", Hidden)!.GetValue(host)! != container)
                throw new Exception("Returning to multiplayer rebuilt its native container.");
            activate.Invoke(host, [false]);
            Console.WriteLine("PASS: startup native host; parked first paint; readiness gate; viewport resize; 6 collapse/expand cycles repair native child size/origin; same HWND across navigation.");
        }
        finally
        {
            host.Dispose();
            if (child != 0) DestroyWindow(child);
            owner.Close();
        }
    }

    private static NativeRect Bounds(nint window, nint parent)
    {
        if (!GetWindowRect(window, out var rect)) throw new Exception("Owned window rectangle unavailable.");
        MapWindowPoints(0, parent, ref rect, 2);
        return rect;
    }

    private static void AssertParked(nint window, nint parent)
    {
        var rect = Bounds(window, parent);
        if (rect.Right >= 0 || rect.Bottom >= 0) throw new Exception($"Inactive/unready native surface leaked into the workspace: ({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom}).");
    }

    private static void AssertBounds(nint window, nint parent, Rect expected)
    {
        var actual = Bounds(window, parent);
        if (actual.Left != (int)expected.X || actual.Top != (int)expected.Y || actual.Right - actual.Left != (int)expected.Width || actual.Bottom - actual.Top != (int)expected.Height)
            throw new Exception($"Native viewport shifted: ({actual.Left},{actual.Top}) vs {expected}.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int exStyle, string name, string title, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from, nint to, ref NativeRect rect, uint count);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
