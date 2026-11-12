using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using BCCL.Core.App;

namespace BCCL;

// One background sample at a time. No frame-loop polling and no per-card samplers.
internal static class GlassLoadMonitor
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    private static readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private static bool initialized, busy;
    private static int revision;
    private static ulong previousIdle, previousTotal;
    private static DateTime gpuRetry;
    public static string Detail { get; private set; } = "正在采样 CPU / GPU…";
    public static void ConfigureIfNeeded()
    {
        var wanted = LiquidGlassTheme.IsEnabled && Config.Preference.GlassAutomatic;
        if (timer.IsEnabled != wanted) Configure();
    }
    public static void Configure()
    {
        revision++;
        if (!initialized) { timer.Tick += Tick; initialized = true; }
        if (LiquidGlassTheme.IsEnabled && Config.Preference.GlassAutomatic) timer.Start();
        else timer.Stop();
    }
    private static async void Tick(object? sender, EventArgs e)
    {
        if (busy || ModMain.frmMain is not { IsVisible:true } form || form.WindowState == WindowState.Minimized) return;
        busy = true;
        var generation = revision;
        try {
            var sample = await Task.Run(Sample);
            if (generation != revision || !Config.Preference.GlassAutomatic || !LiquidGlassTheme.IsEnabled) return;
            var current = LiquidGlassTheme.EffectiveTransparency;
            var next = BCCL.Core.UI.Theme.GlassQualityPolicy.Next(current, sample.cpu, sample.gpu);
            Detail = $"CPU {(sample.cpu.HasValue ? sample.cpu.Value.ToString("0") + "%" : "不可用")} · GPU {(sample.gpu.HasValue ? sample.gpu.Value.ToString("0") + "%" : "监测不可用，保守调节")}";
            LiquidGlassTheme.SetAutomaticLevel(next);
        } catch { Detail = "负载监测暂不可用，使用保守效果"; LiquidGlassTheme.SetAutomaticLevel(25); }
        finally { busy = false; }
    }
    private static (double? cpu, double? gpu) Sample()
    {
        double? cpu = null, gpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user)) {
            var total = kernel + user;
            if (previousTotal != 0 && total > previousTotal) cpu = Math.Clamp(100d * (1d - (double)(idle - previousIdle)/(total-previousTotal)),0,100);
            previousIdle=idle; previousTotal=total;
        }
        if (DateTime.UtcNow < gpuRetry) return (cpu, gpu);
        try {
            using var query = new ManagementObjectSearcher("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            query.Options.Timeout = TimeSpan.FromSeconds(2);
            using var values = query.Get();
            var engines = new Dictionary<string,double>();
            foreach (ManagementObject value in values) {
                using (value) {
                    var name = value["Name"]?.ToString() ?? "";
                    var start = name.IndexOf("luid_",StringComparison.Ordinal);
                    if (start < 0) continue;
                    var key = name[start..]; // Sum process shares of each physical engine, then take the busiest engine.
                    engines[key] = engines.GetValueOrDefault(key) + Convert.ToDouble(value["UtilizationPercentage"]);
                }
            }
            if (engines.Count > 0) gpu = Math.Clamp(engines.Values.Max(),0,100);
            else gpuRetry = DateTime.UtcNow.AddSeconds(30);
        } catch { gpuRetry = DateTime.UtcNow.AddSeconds(30); }
        return (cpu,gpu);
    }
}
