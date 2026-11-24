param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

# Build the rounded silhouette from the unchanged pixel artwork. WPF draws the
# source with nearest-neighbour sampling, and antialiases only the outer clip.
$imageDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../BCCL/Images'))
$cornerRatio = 0.18
function Read-IconBitmap([string]$Path) {
    $bitmap = [Windows.Media.Imaging.BitmapImage]::new()
    $bitmap.BeginInit()
    $bitmap.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bitmap.UriSource = [Uri]::new($Path)
    $bitmap.EndInit()
    $bitmap.Freeze()
    return $bitmap
}
function Render-RoundedIcon($Source, [int]$Size) {
    # Resample explicitly to preserve every interior source pixel. DrawImage's
    # texture sampling can otherwise blend thin pixel-art edges even with
    # BitmapScalingMode.NearestNeighbor on a DrawingVisual.
    $converted = [Windows.Media.Imaging.FormatConvertedBitmap]::new($Source, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $sourceStride = $converted.PixelWidth * 4
    $sourcePixels = [byte[]]::new($sourceStride * $converted.PixelHeight)
    $converted.CopyPixels($sourcePixels, $sourceStride, 0)
    $pixels = [byte[]]::new($Size * $Size * 4)
    for ($y = 0; $y -lt $Size; $y++) {
        $sy = [Math]::Min($converted.PixelHeight - 1, [int][Math]::Floor(($y + 0.5) * $converted.PixelHeight / $Size))
        for ($x = 0; $x -lt $Size; $x++) {
            $sx = [Math]::Min($converted.PixelWidth - 1, [int][Math]::Floor(($x + 0.5) * $converted.PixelWidth / $Size))
            [Buffer]::BlockCopy($sourcePixels, $sy * $sourceStride + $sx * 4, $pixels, ($y * $Size + $x) * 4, 4)
        }
    }
    $nativeSource = [Windows.Media.Imaging.BitmapSource]::Create($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Bgra32, $null, $pixels, $Size * 4)
    $visual = [Windows.Media.DrawingVisual]::new()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::NearestNeighbor)
    $context = $visual.RenderOpen()
    $bounds = [Windows.Rect]::new(0, 0, $Size, $Size)
    $radius = $Size * $cornerRatio
    $context.PushClip([Windows.Media.RectangleGeometry]::new($bounds, $radius, $radius))
    $context.DrawImage($nativeSource, $bounds)
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

$source = Read-IconBitmap (Join-Path $imageDirectory 'icon.png')
[IO.File]::WriteAllBytes((Join-Path $imageDirectory 'icon-rounded.png'), (Render-RoundedIcon $source 256))
[IO.File]::WriteAllBytes((Join-Path $imageDirectory 'icon-rounded-64.png'), (Render-RoundedIcon $source 64))
$splash = Read-IconBitmap (Join-Path $imageDirectory 'splash.png')
[IO.File]::WriteAllBytes((Join-Path $imageDirectory 'splash-rounded.png'), (Render-RoundedIcon $splash 256))

# ICO stores a separate PNG frame for each common Windows DPI/size, including
# 16/20/24px taskbar/title-bar sizes and 256px Explorer thumbnails.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) { ,(Render-RoundedIcon $source $size) }
$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $imageDirectory 'icon.ico'), $stream.ToArray())
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output 'Built rounded PNG/splash and nine ICO sizes from the original BCCL artwork.'
