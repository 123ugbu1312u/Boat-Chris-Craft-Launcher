using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BCCL.Core.App;
using BCCL.Core.Utils.OS;

namespace BCCL;

internal static class CpuOptimizationTool
{
    private sealed record GameRecovery(int ProcessId, long StartTimeUtcTicks, CpuProcessSettings Before);
    private sealed record Settings(bool Enabled, CpuPowerSettings? Before = null, CpuPowerSettings? Target = null, GameRecovery[]? Recovery = null);
    private sealed record GameSettings(GameRecovery Recovery, EventHandler Exited, bool OwnsHandle);
    private static readonly object Gate = new();
    private static readonly Dictionary<Process, GameSettings> Games = new();
    private static readonly string SettingsPath = Path.Combine(Paths.Data, "Optimization", "cpu-performance.json");
    private static Settings? cachedSettings;
    private static string? lastError;
    public static event Action? Changed;

    private static Settings Current
    {
        get
        {
            if (cachedSettings is not null) return cachedSettings;
            var settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? throw new InvalidDataException("Empty CPU optimization settings.")
                : new Settings(false);
            if (settings.Enabled && (settings.Before is null || settings.Target is null))
                throw new InvalidDataException("CPU optimization recovery settings are missing.");
            cachedSettings = settings;
            if (settings.Enabled)
            {
                foreach (var recovery in settings.Recovery ?? [])
                {
                    Process? process = null;
                    try
                    {
                        process = Process.GetProcessById(recovery.ProcessId);
                        // PID reuse must never apply a saved game policy to another application.
                        if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == recovery.StartTimeUtcTicks)
                        {
                            TrackGame(process, recovery, ownsHandle: true);
                            process = null;
                        }
                    }
                    catch (ArgumentException) { } // The saved game has already exited.
                    catch (Exception ex) { lastError = ex.Message; ModBase.Log(ex, "恢复 CPU 优化进程记录失败"); }
                    finally { process?.Dispose(); }
                }
            }
            return cachedSettings;
        }
    }

    private static void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, SettingsPath, true);
            cachedSettings = settings;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string StatusKey()
    {
        lock (Gate)
        {
            try
            {
                if (!Current.Enabled) return "Tools.Test.Cpu.NotOptimized";
                if (lastError is not null) return "Tools.Test.Cpu.Failed";
                if (CpuPerformanceOptimizer.ReadPowerSettings() != Current.Target) return "Tools.Test.Cpu.Changed";
                foreach (var game in Games.Keys.ToArray())
                    if (!game.HasExited && !CpuPerformanceOptimizer.IsProcessOptimized(game)) return "Tools.Test.Cpu.Changed";
                return "Tools.Test.Cpu.Optimized";
            }
            catch (Exception ex) { ModBase.Log(ex, "读取 CPU 优化状态失败"); return "Tools.Test.Cpu.Failed"; }
        }
    }

    public static bool Toggle(Process[] runningGames)
    {
        lock (Gate)
        {
            var previous = Current;
            if (previous.Enabled)
            {
                Restore(previous);
                Save(new Settings(false));
                lastError = null;
                return false;
            }
            var before = CpuPerformanceOptimizer.ReadPowerSettings();
            var enabled = new Settings(true, before, CpuPerformanceOptimizer.PerformanceSettings(before));
            // Persist recovery information before changing Windows, including across a launcher restart.
            Save(enabled);
            try
            {
                CpuPerformanceOptimizer.WritePowerSettings(enabled.Target!);
                foreach (var process in runningGames.Distinct()) ApplyToGameLocked(process);
                lastError = null;
                return true;
            }
            catch (Exception original)
            {
                lastError = original.Message;
                try { Restore(enabled); Save(new Settings(false)); }
                catch (Exception rollback) { throw new AggregateException(original, rollback); }
                throw;
            }
        }
    }

    public static void ApplyToGame(Process process)
    {
        try
        {
            lock (Gate) { if (Current.Enabled) ApplyToGameLocked(process); }
        }
        catch (Exception ex)
        {
            lock (Gate) lastError = ex.Message;
            ModBase.Log(ex, "Minecraft CPU 高性能调度未能生效");
        }
        finally { Changed?.Invoke(); }
    }

    private static void ApplyToGameLocked(Process process)
    {
        if (process.HasExited || Games.Keys.Any(game => game.Id == process.Id)) return;
        var before = CpuPerformanceOptimizer.ReadProcessSettings(process);
        var recovery = new GameRecovery(process.Id, process.StartTime.ToUniversalTime().Ticks, before);
        // Save each game's original state before changing it so restore also works after restarting BCCL.
        Save(Current with { Recovery = (Current.Recovery ?? []).Append(recovery).ToArray() });
        try
        {
            CpuPerformanceOptimizer.OptimizeProcess(process, before);
            TrackGame(process, recovery, ownsHandle: false);
        }
        catch (Exception) when (process.HasExited) { RemoveRecovery(recovery); }
        catch
        {
            if (!process.HasExited) CpuPerformanceOptimizer.RestoreProcessSettings(process, before);
            RemoveRecovery(recovery);
            throw;
        }
    }

    private static void TrackGame(Process process, GameRecovery recovery, bool ownsHandle)
    {
        EventHandler exited = (_, _) =>
        {
            lock (Gate)
            {
                try { ForgetGame(process); }
                catch (Exception ex) { lastError = ex.Message; ModBase.Log(ex, "清理 CPU 优化进程记录失败"); }
            }
            Changed?.Invoke();
        };
        Games.Add(process, new GameSettings(recovery, exited, ownsHandle));
        try
        {
            process.Exited += exited;
            process.EnableRaisingEvents = true;
            if (process.HasExited) ForgetGame(process);
        }
        catch
        {
            ForgetGame(process);
            throw;
        }
    }

    private static void ForgetGame(Process process)
    {
        if (!Games.Remove(process, out var entry)) return;
        process.Exited -= entry.Exited;
        if (entry.OwnsHandle) process.Dispose();
        RemoveRecovery(entry.Recovery);
    }

    private static void RemoveRecovery(GameRecovery recovery) => Save(Current with
    {
        Recovery = (Current.Recovery ?? []).Where(saved => saved != recovery).ToArray()
    });

    private static void Restore(Settings settings)
    {
        var errors = new List<Exception>();
        foreach (var (process, entry) in Games.ToArray())
        {
            try { CpuPerformanceOptimizer.RestoreProcessSettings(process, entry.Recovery.Before); ForgetGame(process); }
            catch (Exception ex) { errors.Add(ex); }
        }
        try { CpuPerformanceOptimizer.RestorePowerSettings(settings.Before!, settings.Target!); }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) { lastError = "Restore incomplete"; throw new AggregateException(errors); }
    }
}
