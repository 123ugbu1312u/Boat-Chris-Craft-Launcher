using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BCCL.Core.Utils.OS;

public sealed record CpuPowerSettings(bool UsesPowerModes, Guid AC, Guid DC);
public sealed record CpuProcessSettings(ProcessPriorityClass Priority, bool PriorityBoost, uint ControlMask, uint StateMask);

/// <summary>Windows performance policies, with readback and reversible process settings. No affinity or clock changes.</summary>
public static partial class CpuPerformanceOptimizer
{
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 1;

    public static CpuPowerSettings ReadPowerSettings()
    {
        try
        {
            var acResult = PowerGetUserConfiguredACPowerMode(out var ac);
            var dcResult = PowerGetUserConfiguredDCPowerMode(out var dc);
            if (acResult == 0 && dcResult == 0) return new(true, ac, dc);
        }
        catch (EntryPointNotFoundException) { } // Older Windows uses a traditional power plan.
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return new(false, Marshal.PtrToStructure<Guid>(pointer), Guid.Empty); }
        finally { LocalFree(pointer); }
    }

    public static CpuPowerSettings PerformanceSettings(CpuPowerSettings original) => original.UsesPowerModes
        ? new(true, BestPerformance, BestPerformance)
        : new(false, HighPerformanceScheme, Guid.Empty);

    public static void WritePowerSettings(CpuPowerSettings settings)
    {
        var ac = settings.AC;
        if (settings.UsesPowerModes)
        {
            var dc = settings.DC;
            Check(PowerSetUserConfiguredACPowerMode(ref ac));
            Check(PowerSetUserConfiguredDCPowerMode(ref dc));
        }
        else Check(PowerSetActiveScheme(IntPtr.Zero, ref ac));
        if (ReadPowerSettings() != settings) throw new InvalidOperationException("Windows power policy readback did not match.");
    }

    /// <summary>Restore only values still owned by this tool; preserve later changes made by the user.</summary>
    public static void RestorePowerSettings(CpuPowerSettings original, CpuPowerSettings applied)
    {
        var current = ReadPowerSettings();
        if (current.UsesPowerModes != applied.UsesPowerModes) return;
        WritePowerSettings(current with
        {
            AC = current.AC == applied.AC ? original.AC : current.AC,
            DC = current.DC == applied.DC ? original.DC : current.DC
        });
    }

    public static CpuProcessSettings ReadProcessSettings(Process process)
    {
        process.Refresh();
        var state = ReadThrottling(process);
        return new(process.PriorityClass, process.PriorityBoostEnabled, state.ControlMask, state.StateMask);
    }

    public static CpuProcessSettings OptimizeProcess(Process process, CpuProcessSettings? original = null)
    {
        var before = original ?? ReadProcessSettings(process);
        try
        {
            // Keep deliberately configured High/RealTime priority. Never force a game onto one core.
            if (before.Priority is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Normal)
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
            process.PriorityBoostEnabled = true;
            SetThrottling(process, before.ControlMask | ExecutionSpeed, before.StateMask & ~ExecutionSpeed);
            if (!IsProcessOptimized(process)) throw new InvalidOperationException("Game CPU policy readback did not match.");
            return before;
        }
        catch
        {
            if (!process.HasExited) RestoreProcessSettings(process, before);
            throw;
        }
    }

    public static bool IsProcessOptimized(Process process)
    {
        var state = ReadProcessSettings(process);
        return (state.Priority is ProcessPriorityClass.AboveNormal or ProcessPriorityClass.High or ProcessPriorityClass.RealTime)
            && state.PriorityBoost && (state.ControlMask & ExecutionSpeed) != 0 && (state.StateMask & ExecutionSpeed) == 0;
    }

    public static void RestoreProcessSettings(Process process, CpuProcessSettings settings)
    {
        if (process.HasExited) return;
        // Attempt both independent restores even when one fails.
        Exception? error = null;
        try { SetThrottling(process, settings.ControlMask, settings.StateMask); }
        catch (Exception ex) { error = ex; }
        try { process.PriorityClass = settings.Priority; process.PriorityBoostEnabled = settings.PriorityBoost; }
        catch (Exception ex) { error = error is null ? ex : new AggregateException(error, ex); }
        if (process.HasExited) return;
        if (error is not null) throw error;
        if (ReadProcessSettings(process) != settings) throw new InvalidOperationException("Game CPU settings could not be restored.");
    }

    private static ThrottlingState ReadThrottling(Process process)
    {
        var state = new ThrottlingState { Version = 1 };
        if (!GetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, 12))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return state;
    }

    private static void SetThrottling(Process process, uint control, uint state)
    {
        var value = new ThrottlingState { Version = 1, ControlMask = control, StateMask = state };
        if (!SetProcessInformation(process.Handle, ProcessPowerThrottling, ref value, 12))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }
    [StructLayout(LayoutKind.Sequential)] private struct ThrottlingState { public uint Version, ControlMask, StateMask; }
    [LibraryImport("powrprof.dll")] private static partial uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [LibraryImport("powrprof.dll")] private static partial uint PowerGetUserConfiguredDCPowerMode(out Guid mode);
    [LibraryImport("powrprof.dll")] private static partial uint PowerSetUserConfiguredACPowerMode(ref Guid mode);
    [LibraryImport("powrprof.dll")] private static partial uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);
    [LibraryImport("powrprof.dll")] private static partial uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [LibraryImport("powrprof.dll")] private static partial uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [LibraryImport("kernel32.dll")] private static partial IntPtr LocalFree(IntPtr memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessInformation(IntPtr process, int infoClass, ref ThrottlingState state, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, ref ThrottlingState state, uint size);
}
