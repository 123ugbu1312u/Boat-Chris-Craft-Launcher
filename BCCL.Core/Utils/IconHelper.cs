using System;
using System.Drawing;
using System.IO;
using BCCL.Core.App;

namespace BCCL.Core.Utils;

public static class IconHelper
{
    public static string GetIconPath()
    {
        // A new executable must get a new URI so Windows notifications do not
        // keep using the square icon cached by an earlier launcher build.
        var paths = Path.Combine(Paths.Temp, $"icon-{File.GetLastWriteTimeUtc(Basics.ExecutablePath).Ticks:x}.png");
        if (!File.Exists(paths))
        {
            CreateIcon(paths);
        }

        return paths;
    }

    private static void CreateIcon(string path)
    {
        using var icon = Icon.ExtractAssociatedIcon(Basics.ExecutablePath) ??
                         throw new InvalidOperationException("无法提取程序图标。");
        using var bitmap = icon.ToBitmap();
        bitmap.Save(path);
    }
}
