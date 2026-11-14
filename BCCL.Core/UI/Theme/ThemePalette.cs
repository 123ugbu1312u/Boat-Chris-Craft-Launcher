using System;
using System.Collections.Generic;
using System.Linq;
using BCCL.Core.App;

namespace BCCL.Core.UI.Theme;

/// <summary>Stable persisted IDs, translated labels and OKLCH accents for both color modes.</summary>
public sealed record ThemePalette(ColorTheme Theme, string NameKey, int Hue,
    double LightAdjust = 0, double ChromaAdjust = 0, bool AprilOnly = false)
{
    public static IReadOnlyList<ThemePalette> All { get; } = Array.AsReadOnly<ThemePalette>([
        new(ColorTheme.SkyBlue, "SkyBlue", 235, 0.36, 0.2),
        new(ColorTheme.CatBlue, "CatBlue", 255, 0, -0.2),
        new(ColorTheme.DeathBlue, "CrashBlue", 268, -0.05, -0.1),
        new(ColorTheme.HmclBlue, "Hmcl", 275, -0.03, -0.35, true),
        new(ColorTheme.Mint, "Mint", 170, 0, -0.15),
        new(ColorTheme.Emerald, "Emerald", 150),
        new(ColorTheme.Forest, "Forest", 130, -0.04, -0.15),
        new(ColorTheme.Amber, "Amber", 85),
        new(ColorTheme.Tangerine, "Tangerine", 55),
        new(ColorTheme.Coral, "Coral", 30, 0, -0.08),
        new(ColorTheme.Rose, "Rose", 5),
        new(ColorTheme.Violet, "Violet", 305),
        new(ColorTheme.Lavender, "Lavender", 285, 0.04, -0.25),
        new(ColorTheme.Slate, "Slate", 245, 0, -0.9),
    ]);

    public string LocalizationKey => "Setup.Ui.Theme.Color." + NameKey;

    public static ThemePalette[] Available(bool aprilFool) =>
        All.Where(palette => aprilFool || !palette.AprilOnly).ToArray();

    public static (int Hue, double LightAdjust, double ChromaAdjust) GetArgs(ColorTheme theme)
    {
        var palette = All.FirstOrDefault(palette => palette.Theme == theme);
        if (palette != null) return (palette.Hue, palette.LightAdjust, palette.ChromaAdjust);
#if DEBUG
        return ((int)theme, 0, 0);
#else
        throw new IndexOutOfRangeException($"Invalid theme index: {(int)theme}");
#endif
    }
}
