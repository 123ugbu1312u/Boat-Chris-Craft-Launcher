using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BCCL.Core.App;

namespace BCCL;

internal static class TaskbarIconService
{
    public static void RefreshPinnedIcons()
    {
        object? shell = null;
        try
        {
            using var resource = ModBase.GetResourceStream("BCCL;component/Images/icon.ico");
            if (resource is null) return;
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BCCL", "Branding");
            var iconPath = Path.Combine(directory, $"icon-{Convert.ToHexString(SHA256.HashData(bytes))}.ico");
            Directory.CreateDirectory(directory);
            if (!File.Exists(iconPath)) File.WriteAllBytes(iconPath, bytes);

            var pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (!Directory.Exists(pins)) return;
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            shell = Activator.CreateInstance(type);
            foreach (var file in Directory.EnumerateFiles(pins, "*.lnk"))
            {
                object? shortcut = null;
                try
                {
                    dynamic link = shortcut = ((dynamic)shell!).CreateShortcut(file);
                    var target = (string)link.TargetPath;
                    if (string.IsNullOrEmpty(target) || !string.Equals(Path.GetFullPath(target),
                            Path.GetFullPath(Basics.ExecutablePath), StringComparison.OrdinalIgnoreCase)) continue;
                    var existing = (string)link.IconLocation;
                    var desired = iconPath + ",0";
                    if (string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase)) continue;
                    // Preserve custom user icons; refresh default icons and icons previously managed here.
                    if (!string.IsNullOrEmpty(existing) && existing != ",0" &&
                        !existing.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(existing, target + ",0", StringComparison.OrdinalIgnoreCase)) continue;
                    link.IconLocation = desired;
                    link.Save();
                    SHChangeNotify(0x2000, 0x1005, file, IntPtr.Zero); // UPDATEITEM, PATHW | FLUSH
                }
                catch (Exception error) { System.Diagnostics.Debug.WriteLine($"BCCL pinned icon: {error.Message}"); }
                finally { if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
            }
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine($"BCCL taskbar icon: {error.Message}"); }
        finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint events, uint flags, string item1, IntPtr item2);
}
