using System.Windows;
using System.Windows.Media;
using BCCL.Core.App;

namespace BCCL;

/// <summary>Shared glass materials; no desktop capture or continuous rendering loop.</summary>
public static class LiquidGlassTheme
{
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
        resources["GlassEdgeBrush"] = new SolidColorBrush(Color.FromArgb(enabled ? (byte)(40 + strength * (dark ? 50 : 170)) : (byte)0, 255, 255, 255));
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
