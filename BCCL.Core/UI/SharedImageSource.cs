using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace BCCL.Core.UI;

/// <summary>Share full-resolution frozen images while in use; the index never keeps pixel buffers alive.</summary>
public static class SharedImageSource
{
    private const int Capacity = 256;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, WeakReference<BitmapSource>> Images = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    public static BitmapSource Load(string source)
    {
        var isResource = source.StartsWith("pack://", StringComparison.OrdinalIgnoreCase);
        var path = isResource ? source : Path.GetFullPath(source);
        var stamp = isResource ? "" : File.GetLastWriteTimeUtc(path).Ticks + ":" + new FileInfo(path).Length;
        var key = path + "|" + stamp;
        lock (Gate)
        {
            if (Images.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached)) return cached;
            BitmapSource image;
            if (isResource)
                image = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            else
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                image = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            }
            image.Freeze();
            if (!Images.ContainsKey(key)) Order.Enqueue(key);
            Images[key] = new WeakReference<BitmapSource>(image);
            while (Images.Count > Capacity) Images.Remove(Order.Dequeue());
            return image;
        }
    }
}
