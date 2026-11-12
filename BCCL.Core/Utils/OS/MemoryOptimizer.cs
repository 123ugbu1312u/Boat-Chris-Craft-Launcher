using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace BCCL.Core.Utils.OS;

public readonly record struct MemoryOptimizationResult(ulong AvailableBefore, ulong AvailableAfter)
{
    public long AvailableChange => checked((long)AvailableAfter - (long)AvailableBefore);

    // Compatibility with the supplied launcher's --memory: return the increase in KiB, zero if it decreased.
    public int ToExitCode() => (int)Math.Clamp(Math.Round(Math.Max(0, AvailableChange) / 1024d), 0, int.MaxValue);
}

public interface IMemoryOptimizationBackend
{
    bool IsAdministrator { get; }
    ulong GetAvailableMemory();
    IDisposable EnablePrivilege(string privilege);
    void ExecuteCommand(int informationClass, int command);
}

/// <summary>
/// Port of PCL.PageOtherTest.MemoryOptimizeInternal from the user-supplied executable.
/// Preserve privilege name, information class 80 and commands 2, 3, 4 in that order.
/// See THIRD-PARTY-MEMORY.md for source identity and adaptations.
/// </summary>
public sealed class MemoryOptimizationRunner(IMemoryOptimizationBackend backend)
{
    private int _running;

    public MemoryOptimizationResult Run()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("Memory optimization is already running.");
        try
        {
            if (!backend.IsAdministrator) throw new UnauthorizedAccessException("Memory optimization requires administrator permission.");
            var before = backend.GetAvailableMemory();
            using (backend.EnablePrivilege("SeProfileSingleProcessPrivilege"))
                for (var command = 2; command <= 4; command++)
                    backend.ExecuteCommand(80, command);
            return new(before, backend.GetAvailableMemory());
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}

public static partial class MemoryOptimizer
{
    public static MemoryOptimizationResult Run()
    {
        // Separate from the launcher single-instance lock so an elevated --memory helper can coexist with its UI.
        using var mutex = new Mutex(false, @"Local\BCCL.MemoryOptimization");
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) throw new InvalidOperationException("Memory optimization is already running.");
        try { return new MemoryOptimizationRunner(new WindowsBackend()).Run(); }
        finally { mutex.ReleaseMutex(); }
    }

    private sealed class WindowsBackend : IMemoryOptimizationBackend
    {
        public bool IsAdministrator => ProcessInterop.IsAdmin();
        public ulong GetAvailableMemory() => KernelInterop.GetAvailablePhysicalMemoryBytes();

        public IDisposable EnablePrivilege(string privilege)
        {
            var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);
            try
            {
                if (!LookupPrivilegeValue(null, privilege, out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                if (!AdjustTokenPrivileges(identity.Token, false, ref state, Marshal.SizeOf<TokenPrivileges>(), out var previous, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var error = Marshal.GetLastWin32Error();
                if (error != 0) throw new Win32Exception(error);
                return new PrivilegeLease(identity, previous);
            }
            catch { identity.Dispose(); throw; }
        }

        public void ExecuteCommand(int informationClass, int command)
        {
            var status = NtSetSystemInformation(informationClass, ref command, sizeof(int));
            if (status != 0) throw new InvalidOperationException($"Memory optimization operation {command} failed (NTSTATUS 0x{status:X8}).");
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    private sealed class PrivilegeLease(WindowsIdentity identity, TokenPrivileges previous) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (!RestoreTokenPrivileges(identity.Token, false, ref previous, 0, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var error = Marshal.GetLastWin32Error();
                if (error != 0) throw new Win32Exception(error);
            }
            finally { identity.Dispose(); }
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges state, int length, out TokenPrivileges previous, out int requiredLength);

    [LibraryImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RestoreTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges state, int length, IntPtr previous, IntPtr requiredLength);

    [LibraryImport("ntdll.dll")]
    private static partial uint NtSetSystemInformation(int informationClass, ref int command, int length);
}
