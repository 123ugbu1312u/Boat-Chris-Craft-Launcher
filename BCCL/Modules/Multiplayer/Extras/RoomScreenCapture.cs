using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using BCCL.Core.Link.MCTier;

namespace BCCL.Multiplayer;

/// <summary>Owned by one capture thread; reuses GDI surfaces and scales before copying pixels into WIC.</summary>
internal sealed class RoomScreenCapture : IDisposable
{
    private nint desktop, memory, bitmap, previous;
    private int width, height, outputWidth, outputHeight;
    private RoomDxgiCapture? accelerated;
    private bool attemptedAcceleration;
    public string Backend => accelerated is null ? "GDI" : "DXGI";
    private long gdiFrames;
    public long FreshFrames => accelerated?.FreshFrames ?? gdiFrames;

    public ExtrasPacket Capture()
    {
        if (!attemptedAcceleration)
        {
            attemptedAcceleration = true;
            try { accelerated = new RoomDxgiCapture(); } catch (Exception) { }
        }
        if (accelerated is not null)
        {
            try { return accelerated.Capture(); }
            catch (Exception) { accelerated.Dispose(); accelerated = null; }
        }
        var nextWidth = GetSystemMetrics(0); var nextHeight = GetSystemMetrics(1);
        if (nextWidth <= 0 || nextHeight <= 0) throw new IOException("无法读取主显示器。");
        if (width != nextWidth || height != nextHeight || bitmap == 0)
        {
            ReleaseGdi(); width = nextWidth; height = nextHeight;
            var scale = Math.Min(1d, Math.Min(960d / width, 540d / height));
            outputWidth = Math.Max(1, (int)Math.Round(width * scale));
            outputHeight = Math.Max(1, (int)Math.Round(height * scale));
            desktop = GetDC(0); memory = CreateCompatibleDC(desktop);
            bitmap = CreateCompatibleBitmap(desktop, outputWidth, outputHeight);
            if (desktop == 0 || memory == 0 || bitmap == 0) { Dispose(); throw new IOException("无法准备屏幕采集。"); }
            previous = SelectObject(memory, bitmap);
            SetStretchBltMode(memory, 3);
        }
        if (!StretchBlt(memory, 0, 0, outputWidth, outputHeight, desktop, 0, 0, width, height, 0x40CC0020))
            throw new IOException("无法捕获屏幕。");
        var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var encoder = new JpegBitmapEncoder { QualityLevel = 65 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var data = new MemoryStream(); encoder.Save(data);
        gdiFrames++;
        return new ExtrasPacket("screen", Size: width, Offset: height, Data: data.ToArray());
    }

    public void Dispose()
    {
        accelerated?.Dispose(); accelerated = null;
        ReleaseGdi();
    }
    private void ReleaseGdi()
    {
        if (previous != 0 && memory != 0) SelectObject(memory, previous);
        if (bitmap != 0) DeleteObject(bitmap);
        if (memory != 0) DeleteDC(memory);
        if (desktop != 0) ReleaseDC(0, desktop);
        previous = bitmap = memory = desktop = 0;
    }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, int sw, int sh, uint operation);
}
