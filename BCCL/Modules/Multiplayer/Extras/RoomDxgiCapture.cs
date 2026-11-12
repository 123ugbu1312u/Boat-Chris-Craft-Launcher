using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCCL.Core.Link.MCTier;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace BCCL.Multiplayer;

/// <summary>Desktop Duplication avoids GDI's synchronous compositor readback on each frame.</summary>
internal sealed class RoomDxgiCapture : IDisposable
{
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? staging;
    private ID3D11Texture2D? scaled;
    private ID3D11ShaderResourceView? scaledView;
    private uint sourceWidth, sourceHeight, mipLevel;
    private ExtrasPacket? lastFrame;
    private readonly int width, height;
    private readonly ModeRotation rotation;
    public long FreshFrames { get; private set; }

    public RoomDxgiCapture()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
            using (adapter)
            {
                for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                using (output)
                {
                    var description = output.Description;
                    if (!description.AttachedToDesktop || description.DesktopCoordinates.Left != 0 || description.DesktopCoordinates.Top != 0) continue;
                    width = description.DesktopCoordinates.Right; height = description.DesktopCoordinates.Bottom; rotation = description.Rotation;
                    D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                        [FeatureLevel.Level_11_0], out ID3D11Device nextDevice, out ID3D11DeviceContext nextContext).CheckError();
                    device = nextDevice; context = nextContext;
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    duplication = output1.DuplicateOutput(device);
                    return;
                }
            }
            throw new IOException("当前显示器不支持桌面复制。");
        }
        catch { Dispose(); throw; }
    }

    public ExtrasPacket Capture()
    {
        var result = duplication!.AcquireNextFrame(lastFrame is null ? 100u : 0u, out var information, out var resource);
        if (result.Code == unchecked((int)0x887A0027) && lastFrame is not null) return lastFrame;
        result.CheckError();
        FreshFrames++;
        try
        {
            using (resource)
            using (var texture = resource.QueryInterface<ID3D11Texture2D>())
            {
                if (information.LastPresentTime == 0 && lastFrame is not null) return lastFrame;
                var description = texture.Description;
                if (staging is null || sourceWidth != description.Width || sourceHeight != description.Height)
                {
                    staging?.Dispose(); scaledView?.Dispose(); scaled?.Dispose(); scaledView = null; scaled = null;
                    sourceWidth = description.Width; sourceHeight = description.Height; mipLevel = 0;
                    var rotated = rotation is ModeRotation.Rotate90 or ModeRotation.Rotate270;
                    var effectiveWidth = rotated ? sourceHeight : sourceWidth;
                    var effectiveHeight = rotated ? sourceWidth : sourceHeight;
                    if ((device!.CheckFormatSupport(description.Format) & FormatSupport.MipAutogen) != 0)
                    {
                        while ((effectiveWidth >> (int)mipLevel) > 960 || (effectiveHeight >> (int)mipLevel) > 540) mipLevel++;
                    }
                    if (mipLevel > 0)
                    {
                        var gpuDescription = description;
                        gpuDescription.MipLevels = 0; gpuDescription.Usage = ResourceUsage.Default;
                        gpuDescription.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
                        gpuDescription.CPUAccessFlags = CpuAccessFlags.None; gpuDescription.MiscFlags = ResourceOptionFlags.GenerateMips;
                        scaled = device.CreateTexture2D(gpuDescription); scaledView = device.CreateShaderResourceView(scaled);
                        description.Width = Math.Max(1, sourceWidth >> (int)mipLevel);
                        description.Height = Math.Max(1, sourceHeight >> (int)mipLevel);
                    }
                    description.MipLevels = 1;
                    description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None;
                    description.CPUAccessFlags = CpuAccessFlags.Read; description.MiscFlags = ResourceOptionFlags.None;
                    staging = device!.CreateTexture2D(description);
                }
                if (scaled is not null)
                {
                    context!.CopySubresourceRegion(scaled, 0, 0, 0, 0, texture, 0);
                    context.GenerateMips(scaledView!);
                    context.CopySubresourceRegion(staging, 0, 0, 0, 0, scaled, mipLevel);
                }
                else context!.CopyResource(staging, texture);
                description = staging.Description;
                context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
                try
                {
                    BitmapSource source = BitmapSource.Create((int)description.Width, (int)description.Height, 96, 96, PixelFormats.Bgra32,
                        null, mapped.DataPointer, checked((int)(mapped.RowPitch * description.Height)), (int)mapped.RowPitch);
                    var angle = rotation switch { ModeRotation.Rotate90 => 90, ModeRotation.Rotate180 => 180, ModeRotation.Rotate270 => 270, _ => 0 };
                    if (angle != 0) source = new TransformedBitmap(source, new RotateTransform(angle));
                    var scale = Math.Min(1d, Math.Min(960d / source.PixelWidth, 540d / source.PixelHeight));
                    if (scale < 1) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                    var encoder = new JpegBitmapEncoder { QualityLevel = 65 };
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    using var data = new MemoryStream(); encoder.Save(data);
                    return lastFrame = new ExtrasPacket("screen", Size: width, Offset: height, Data: data.ToArray());
                }
                finally { context.Unmap(staging, 0); }
            }
        }
        finally { duplication.ReleaseFrame(); }
    }

    public void Dispose()
    {
        lastFrame = null;
        staging?.Dispose(); staging = null;
        scaledView?.Dispose(); scaledView = null; scaled?.Dispose(); scaled = null;
        duplication?.Dispose(); duplication = null;
        context?.Dispose(); context = null;
        device?.Dispose(); device = null;
    }
}
