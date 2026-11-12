using System.Diagnostics;
using System.Text.Json;
using BCCL;
using BCCL.Core.App;
using BCCL.Core.Utils.OS;

if (args.FirstOrDefault() == "--child") { Console.ReadLine(); return; }
if (args.FirstOrDefault() == "--persisted")
{
    Paths.Data = args[1];
    if (CpuOptimizationTool.StatusKey() != "Tools.Test.Cpu.Optimized") throw new Exception("Persisted state lost");
    Console.WriteLine("PASS: persisted state re-read by a new process");
    return;
}
if (args.FirstOrDefault() == "--enable-restart")
{
    Paths.Data = args[1];
    using var game = Process.GetProcessById(int.Parse(args[2]));
    if (!CpuOptimizationTool.Toggle([game])) throw new Exception("Producer did not enable policy");
    return; // Simulate closing BCCL while Minecraft continues running.
}
if (args.FirstOrDefault() == "--restore-restart")
{
    Paths.Data = args[1];
    if (CpuOptimizationTool.StatusKey() != "Tools.Test.Cpu.Optimized") throw new Exception("Game recovery failed");
    if (CpuOptimizationTool.Toggle([])) throw new Exception("Consumer did not disable policy");
    return;
}
if (args.FirstOrDefault() != "--native") throw new Exception("Pass --native to run reversible Windows power-policy checks.");
var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
Paths.Data = directory;
var originalPower = CpuPerformanceOptimizer.ReadPowerSettings();
File.WriteAllText(Path.Combine(directory, "power-before.json"), JsonSerializer.Serialize(originalPower));
var children = new List<Process>();
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++; Console.WriteLine("PASS: " + name);
}
Process Child(params string[] arguments)
{
    var info = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        WindowStyle = ProcessWindowStyle.Hidden
    };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    var child = Process.Start(info)!;
    children.Add(child);
    return child;
}
try
{
    Check(CpuOptimizationTool.StatusKey() == "Tools.Test.Cpu.NotOptimized", "initial state is not optimized");
    using var invalid = new Process();
    try { CpuOptimizationTool.Toggle([invalid]); throw new Exception("Invalid process was accepted"); }
    catch (InvalidOperationException) { }
    Check(CpuPerformanceOptimizer.ReadPowerSettings() == originalPower, "failed enable rolls back Windows power settings");
    Check(CpuOptimizationTool.StatusKey() != "Tools.Test.Cpu.Optimized", "failed enable never reports success");

    var game = Child("--child");
    var before = CpuPerformanceOptimizer.ReadProcessSettings(game);
    var affinity = game.ProcessorAffinity;
    Check(CpuOptimizationTool.Toggle([game]), "combined optimization enabled");
    Check(CpuPerformanceOptimizer.ReadPowerSettings() == CpuPerformanceOptimizer.PerformanceSettings(originalPower), "both AC/DC power modes read back correctly");
    Check(CpuPerformanceOptimizer.IsProcessOptimized(game), "running game priority, boost and HighQoS verified");
    Check(game.ProcessorAffinity == affinity, "all original CPU affinity remains available");
    Check(CpuOptimizationTool.StatusKey() == "Tools.Test.Cpu.Optimized", "optimized label follows verified state");

    var fresh = Child("--persisted", directory);
    if (!fresh.WaitForExit(10000)) throw new Exception("Persistence check timed out");
    Check(fresh.ExitCode == 0, "new process reads persisted optimized state");
    var second = Child("--child");
    var secondBefore = CpuPerformanceOptimizer.ReadProcessSettings(second);
    CpuOptimizationTool.ApplyToGame(second);
    Check(CpuPerformanceOptimizer.IsProcessOptimized(second), "future launch hook applies game CPU policy");
    Check(!CpuOptimizationTool.Toggle([]), "second click disables the policy");
    Check(CpuPerformanceOptimizer.ReadPowerSettings() == originalPower, "original Windows power settings restored");
    Check(CpuPerformanceOptimizer.ReadProcessSettings(game) == before, "running game settings restored exactly");
    Check(CpuPerformanceOptimizer.ReadProcessSettings(second) == secondBefore, "later game settings restored exactly");

    game.PriorityClass = ProcessPriorityClass.High;
    var highBefore = CpuPerformanceOptimizer.ReadProcessSettings(game);
    var highSnapshot = CpuPerformanceOptimizer.OptimizeProcess(game);
    Check(game.PriorityClass == ProcessPriorityClass.High, "existing High priority is never reduced");
    CpuPerformanceOptimizer.RestoreProcessSettings(game, highSnapshot);
    Check(CpuPerformanceOptimizer.ReadProcessSettings(game) == highBefore, "existing High-priority state restored");
    CpuPerformanceOptimizer.RestoreProcessSettings(game, before);

    CpuOptimizationTool.Toggle([]);
    CpuPerformanceOptimizer.WritePowerSettings(originalPower); // Simulate an explicit external/user power-mode change.
    Check(CpuOptimizationTool.StatusKey() == "Tools.Test.Cpu.Changed", "external setting changes clear optimized status");
    CpuOptimizationTool.Toggle([]);
    Check(CpuPerformanceOptimizer.ReadPowerSettings() == originalPower, "external power-mode changes are respected on disable");
    var producer = Child("--enable-restart", directory, game.Id.ToString());
    if (!producer.WaitForExit(10000)) throw new Exception("Restart producer timed out");
    Check(producer.ExitCode == 0 && CpuPerformanceOptimizer.IsProcessOptimized(game), "game policy survives launcher-process exit");
    var consumer = Child("--restore-restart", directory);
    if (!consumer.WaitForExit(10000)) throw new Exception("Restart consumer timed out");
    Check(consumer.ExitCode == 0, "new launcher process recovers game records and disables policy");
    Check(CpuPerformanceOptimizer.ReadProcessSettings(game) == before, "game original settings restored across launcher restart");
    Check(CpuPerformanceOptimizer.ReadPowerSettings() == originalPower, "Windows original settings restored across launcher restart");
    Console.WriteLine($"PASS: {checks} native CPU/power checks; original power settings restored.");
}
finally
{
    CpuPerformanceOptimizer.WritePowerSettings(originalPower);
    foreach (var child in children)
    {
        if (!child.HasExited)
        {
            child.StandardInput.WriteLine();
            if (!child.WaitForExit(3000)) { child.Kill(); child.WaitForExit(); }
        }
        child.Dispose();
    }
}

namespace BCCL
{
    // The production coordinator is compiled above; only launcher logging is replaced in this isolated harness.
    internal static class ModBase { public static void Log(Exception exception, string message) => Console.Error.WriteLine(message + ": " + exception.Message); }
}
