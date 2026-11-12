using System;
namespace BCCL.Core.UI.Theme;

/// <summary>Reduce optical work promptly under pressure, recover slowly to prevent oscillation.</summary>
public static class GlassQualityPolicy
{
    public static int Next(int current, double? cpu, double? gpu)
    {
        current = Math.Clamp(current, 0, 100);
        var load = Math.Max(cpu ?? 100, gpu ?? 60);
        var target = load >= 90 ? 10 : load >= 75 ? 25 : load >= 60 ? 45 : load >= 40 ? 65 : 85;
        if (!cpu.HasValue || !gpu.HasValue) target = Math.Min(target, 35);
        return current + Math.Clamp(target - current, -20, 5);
    }
}

