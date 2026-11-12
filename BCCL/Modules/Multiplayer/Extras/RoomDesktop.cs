using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCCL.Core.Link.MCTier;

namespace BCCL.Multiplayer;

/// <summary>Primary-display sharing and revocable input. No input is applied without a live explicit grant.</summary>
internal sealed class RoomDesktop : IDisposable
{
    private HwndSource? hotkeyWindow;
    private string controller = "", grant = "";
    private DateTime expires;
    private readonly HashSet<ushort> heldKeys = new();
    private bool leftHeld, rightHeld;
    private long lastInput;
    public event Action? Revoked;
    public bool Sharing { get; set; }
    public string Controller => controller;

    public ExtrasPacket Capture()
    {
        if (!Sharing) throw new InvalidOperationException("尚未开启屏幕共享。");
        using var source = new RoomScreenCapture();
        return source.Capture();
    }

    public string Allow(string ip)
    {
        if (!Sharing) throw new InvalidOperationException("请先主动开启屏幕共享，再允许远程控制。");
        Revoke();
        hotkeyWindow = new HwndSource(new HwndSourceParameters("BCCL Remote Stop") { ParentWindow = new nint(-3), WindowStyle = 0 });
        hotkeyWindow.AddHook(Hotkey);
        if (!RegisterHotKey(hotkeyWindow.Handle, 1, 0x4003, 0x7B))
        { hotkeyWindow.Dispose(); hotkeyWindow = null; throw new IOException("停止快捷键 Ctrl+Alt+F12 已被占用，请先释放此快捷键。"); }
        controller = ip;
        grant = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        expires = DateTime.UtcNow.AddMinutes(15);
        return grant;
    }

    private nint Hotkey(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam == 1) { Revoke(); handled = true; }
        return 0;
    }
    public void CheckExpiry() { if (controller.Length > 0 && DateTime.UtcNow > expires) Revoke(); }

    public bool Apply(string ip, ExtrasPacket packet)
    {
        if (!Sharing || ip != controller || grant.Length == 0 || packet.Id != grant) return false;
        if (DateTime.UtcNow > expires) { Revoke(); return false; }
        if (packet.X is < 0 or > 1 || packet.Y is < 0 or > 1 || !double.IsFinite(packet.X) || !double.IsFinite(packet.Y)) return false;
        // Mouse movement is coalesced. Key/button releases must never be throttled.
        if (packet.Text == "move" && Environment.TickCount64 - lastInput < 15) return false;
        lastInput = Environment.TickCount64;
        if (packet.Text is "move" or "left-down" or "left-up" or "right-down" or "right-up" or "wheel")
        {
            var flags = 0x8001u;
            flags |= packet.Text switch { "left-down" => 0x0002u, "left-up" => 0x0004u, "right-down" => 0x0008u, "right-up" => 0x0010u, "wheel" => 0x0800u, _ => 0u };
            if (packet.Text == "left-down") leftHeld = true;
            if (packet.Text == "left-up") leftHeld = false;
            if (packet.Text == "right-down") rightHeld = true;
            if (packet.Text == "right-up") rightHeld = false;
            var input = new INPUT { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { X = (int)(packet.X * 65535), Y = (int)(packet.Y * 65535), Flags = flags, MouseData = packet.Text == "wheel" ? unchecked((uint)Math.Clamp(packet.Code, -1200, 1200)) : 0 } } };
            SendInput(1, [input], Marshal.SizeOf<INPUT>());
            return true;
        }
        if (packet.Text is not ("key-down" or "key-up") || packet.Code is < 8 or > 254 || packet.Code is 0x5B or 0x5C) return false;
        var key = (ushort)packet.Code;
        if (packet.Text == "key-down") heldKeys.Add(key); else heldKeys.Remove(key);
        SendKey(key, packet.Text == "key-up"); return true;
    }

    public void Revoke()
    {
        var hadController = controller.Length > 0;
        controller = ""; grant = "";
        foreach (var key in heldKeys) SendKey(key, true);
        heldKeys.Clear();
        if (leftHeld || rightHeld)
            SendInput(1, [new INPUT { Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = (leftHeld ? 4u : 0) | (rightHeld ? 16u : 0) } } }], Marshal.SizeOf<INPUT>());
        leftHeld = rightHeld = false;
        if (hotkeyWindow is not null) { UnregisterHotKey(hotkeyWindow.Handle, 1); hotkeyWindow.Dispose(); hotkeyWindow = null; }
        if (hadController) Revoked?.Invoke();
    }

    private static void SendKey(ushort key, bool release)
    {
        var extended = key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0xA3 or 0xA5;
        SendInput(1, [new INPUT { Type = 1, Data = new InputUnion { Keyboard = new KEYBDINPUT { Key = key, Flags = (release ? 2u : 0) | (extended ? 1u : 0) } } }], Marshal.SizeOf<INPUT>());
    }

    public void Dispose() { Sharing = false; Revoke(); }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
