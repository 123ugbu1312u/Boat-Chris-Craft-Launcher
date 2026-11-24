using System.Windows;
using System.Windows.Media;
using BCCL.Core.App;
using BCCL.Core.App.Localization;

namespace BCCL;

/// <summary>Shared glass materials; no desktop capture or continuous rendering loop.</summary>
public static class LiquidGlassTheme
{
    private static readonly (string Name, Color Tint)[] ButtonBorderPalette =
    [
        ("Silver", Colors.Transparent),
        ("White", Colors.White),
        ("Slate", Color.FromRgb(121, 145, 170)),
        ("Charcoal", Color.FromRgb(65, 73, 87)),
        ("Rose", Color.FromRgb(247, 112, 169)),
        ("Coral", Color.FromRgb(245, 92, 81)),
        ("Tangerine", Color.FromRgb(255, 134, 47)),
        ("Amber", Color.FromRgb(242, 176, 44)),
        ("Lemon", Color.FromRgb(239, 226, 76)),
        ("Forest", Color.FromRgb(79, 140, 60)),
        ("Emerald", Color.FromRgb(57, 204, 104)),
        ("Mint", Color.FromRgb(75, 230, 192)),
        ("SkyBlue", Color.FromRgb(75, 197, 247)),
        ("Cobalt", Color.FromRgb(79, 127, 247)),
        ("Violet", Color.FromRgb(169, 82, 244)),
        ("Lavender", Color.FromRgb(189, 161, 240)),
    ];

    internal static string[] ButtonBorderColorNames => ButtonBorderPalette
        .Select(entry => Lang.Text("Setup.Ui.Theme.Color." + entry.Name)).ToArray();

    internal static int ButtonBorderColorIndex
    {
        get
        {
            var index = Config.Preference.GlassButtonBorderColor;
            return index >= 0 && index < ButtonBorderPalette.Length ? index : 0;
        }
    }

    internal static void ApplyBorders()
    {
        var resources = ThemeManager.AppResources;
        var visibility = Config.Preference.GlassButtonBorder ? Visibility.Visible : Visibility.Collapsed;
        if (!Equals(resources["GlassButtonBorderVisibility"], visibility))
            resources["GlassButtonBorderVisibility"] = visibility;
        var tint = ButtonBorderPalette[ButtonBorderColorIndex].Tint;
        var color = tint.A == 0 ? Colors.White : tint;
        var strength = EffectiveTransparency / 100d;
        var cardEdgeAlpha = visibility == Visibility.Visible && IsEnabled
            ? (byte)(40 + strength * (ThemeManager.IsDarkMode ? 50 : 170))
            : (byte)0;
        var cardEdge = Color.FromArgb(cardEdgeAlpha, color.R, color.G, color.B);
        if (resources["GlassEdgeBrush"] is not SolidColorBrush edgeBrush || edgeBrush.Color != cardEdge)
        {
            var brush = new SolidColorBrush(cardEdge);
            brush.Freeze();
            resources["GlassEdgeBrush"] = brush;
        }

        if (resources["GlassButtonBorderTint"] is Color current && current == tint) return;
        resources["GlassButtonBorderTint"] = tint;

        var outline = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0xf2, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0x35, color.R, color.G, color.B), 0.48),
                new GradientStop(Color.FromArgb(0xa6, color.R, color.G, color.B), 1),
            ],
        };
        outline.Freeze();
        resources["GlassNavigationButtonBorderBrush"] = outline;
    }

    public static bool IsEnabled => Config.Preference.LiquidGlass && !SystemParameters.HighContrast;

    public static event Action? Changed;
    public static int EffectiveTransparency { get; private set; } = 60;
    public static string Status => !IsEnabled ? "Liquid Glass 已关闭" : Config.Preference.GlassAutomatic
        ? $"自动监管 · 透明效果 {EffectiveTransparency}% · {GlassLoadMonitor.Detail}"
        : $"透明效果 {EffectiveTransparency}% · 中央文字保持清晰，边缘折射与光影随强度变化";
    public static void SettingsChanged()
    {
        ModBase.RunInUi(() => {
            EffectiveTransparency = Config.Preference.GlassAutomatic ? Math.Min(EffectiveTransparency, 60) : Math.Clamp(Config.Preference.GlassTransparency, 0, 100);
            GlassLoadMonitor.Configure();
            Apply();
        });
    }
    public static void SetAutomaticLevel(int value)
    {
        var next = Math.Clamp(value, 0, 100);
        if (next == EffectiveTransparency) { Changed?.Invoke(); return; }
        EffectiveTransparency = next;
        Apply();
    }
    public static void Apply()
    {
        var resources = ThemeManager.AppResources;
        var enabled = IsEnabled;
        if (!Config.Preference.GlassAutomatic) EffectiveTransparency = Math.Clamp(Config.Preference.GlassTransparency, 0, 100);
        var strength = enabled ? EffectiveTransparency / 100d : 0d;
        var glassButtons = enabled && Config.Preference.GlassButtons;
        resources["GlassButtonsEnabled"] = glassButtons;
        resources["CapsuleButtonsEnabled"] = Config.Preference.CapsuleButtons;
        resources["GlassButtonVisibility"] = glassButtons ? Visibility.Visible : Visibility.Collapsed;
        ApplyBorders();
        resources["GlassStrength"] = strength;
        resources["GlassSearchButtonCornerRadius"] = new CornerRadius(enabled ? 12d : 3d);
        GlassLoadMonitor.ConfigureIfNeeded();
        var dark = ThemeManager.IsDarkMode;
        var baseColor = WorkbenchTheme.Apply(dark, SystemParameters.HighContrast);
        // The scene already uses soft gradients. Re-sampling the complete visual tree
        // in every card duplicates work and causes recursive backdrop rendering.
        const double blur = 0d;

        SolidColorBrush Tint(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            return brush;
        }
        resources["LiquidGlassVisibility"] = enabled ? Visibility.Visible : Visibility.Collapsed;
        resources["GlassBlurRadius"] = blur;
        resources["GlassCardBlurRadius"] = enabled ? blur : resources["BlurRadius"];
        resources["GlassNavigationBackground"] = Brushes.Transparent;
        resources["GlassHeaderBackground"] = enabled ? Brushes.Transparent : Tint(255);
        resources["GlassShellFill"] = Tint((byte)(235 - strength * 100));
        resources["GlassLeftFill"] = enabled ? Tint((byte)(240 - strength * 90)) : resources["ColorBrushTransparentBackground"];
        resources["GlassCardFill"] = Tint(enabled ? (byte)(248 - strength * 65) : (byte)255);
        resources["GlassButtonFill"] = Tint((byte)(100 - strength * 55));
        SolidColorBrush ButtonHover(string colorKey)
        {
            var color = ((SolidColorBrush)resources[colorKey]).Color;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(145 - strength * 35), color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }
        resources["GlassButtonHoverFill"] = ButtonHover("ColorBrush7");
        resources["GlassButtonRedHoverFill"] = ButtonHover("ColorBrushRedBack");
        resources["GlassNavigationForeground"] = enabled ? resources["ColorBrush1"] : Brushes.White;

        var form = ModMain.frmMain;
        Changed?.Invoke();
        if (form?.PanTitleSelect is null) return;
        foreach (var button in form.PanTitleSelect.Children.OfType<MyRadioButton>())
            button.RefreshMyRadioButtonColor();
        foreach (var button in new[] { form.BtnTitleMin, form.BtnTitleClose, form.BtnTitleHelp })
        {
            if (button is null) continue;
            button.Theme = MyIconButton.Themes.Black; // Uses dark glyphs on a light header and light glyphs in dark mode.
            button.RefreshAnim();
        }
    }
}
