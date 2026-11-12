using System.Windows.Media;

namespace BCCL;

/// <summary>Neutral workspace materials layered with the existing interactive glass.</summary>
internal static class WorkbenchTheme
{
    public static Color Apply(bool dark, bool highContrast)
    {
        var resources = ThemeManager.AppResources;
        Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        void Brush(string key, string hex)
        {
            var value = new SolidColorBrush(ColorOf(hex));
            value.Freeze();
            resources[key] = value;
        }
        var background = highContrast
            ? ((SolidColorBrush)resources["ColorBrushBackground"]).Color
            : ColorOf(dark ? "#181D26" : "#F1F5FA");
        var scene = new SolidColorBrush(background);
        scene.Freeze();
        resources["WorkbenchSceneFill"] = scene;
        resources["WorkbenchSceneTop"] = highContrast ? background : ColorOf(dark ? "#111720" : "#F5F8FC");
        resources["WorkbenchSceneBottom"] = highContrast ? background : ColorOf(dark ? "#202B38" : "#DEEAF3");
        // Keep moving page text from showing through the expanded navigation rail.
        Brush("WorkbenchRailFill", dark ? "#FF18202B" : "#FFEAF1F8");
        Brush("WorkbenchContextFill", dark ? "#C8212B38" : "#CEFFFFFF");
        Brush("WorkbenchDivider", dark ? "#263ED2F5" : "#220F3552");
        Brush("WorkbenchAccentFill", dark ? "#183ED2F5" : "#16328AA6");
        if (highContrast)
        {
            resources["WorkbenchRailFill"] = resources["ColorBrushBackground"];
            resources["WorkbenchContextFill"] = resources["ColorBrushBackground"];
            resources["WorkbenchDivider"] = resources["ColorBrushGray1"];
        }
        return background;
    }
}
