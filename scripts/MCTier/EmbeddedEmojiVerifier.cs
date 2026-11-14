using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace BCCL.Build.Multiplayer;

/// <summary>Reads the actual Windows resource without running the component or its DLL imports.</summary>
public static class EmbeddedEmojiVerifier
{
    public static string Verify(string executable, string standalonePack)
    {
        // Map resources only: do not execute the image, resolve imports, or initialize a process.
        var module = LoadLibraryEx(executable, IntPtr.Zero, 0x2 | 0x20);
        if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var resource = FindResource(module, "MCTIER_BUILTIN_EMOJI_PACK", new IntPtr(11));
            if (resource == IntPtr.Zero) throw new InvalidDataException("Component has no embedded emoji pack.");
            var size = SizeofResource(module, resource);
            var expectedFile = new FileInfo(standalonePack);
            if (size == 0 || size > int.MaxValue || size != expectedFile.Length)
                throw new InvalidDataException("Embedded emoji pack size does not match the original.");
            var loaded = LoadResource(module, resource);
            var pointer = LockResource(loaded);
            if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            var actual = Convert.ToHexString(SHA256.HashData(bytes));
            using var original = File.OpenRead(standalonePack);
            var expected = Convert.ToHexString(SHA256.HashData(original));
            if (actual != expected) throw new InvalidDataException("Embedded emoji pack checksum mismatch.");
            return actual;
        }
        finally { FreeLibrary(module); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, string name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
}
