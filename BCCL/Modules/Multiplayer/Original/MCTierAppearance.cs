using System.IO;
using System.Text.Json;
using System.Windows.Media;
using BCCL.Core.App;

namespace BCCL.Multiplayer.Original;

/// <summary>Publishes only the launcher's current palette to its embedded workspace.</summary>
internal static class MCTierAppearance
{
    private static string? published;
    public static string FilePath => Path.Combine(Paths.Data, "Multiplayer", "Original", "appearance.json");
    public static Color BackgroundColor => ThemeManager.AppResources["WorkbenchSceneFill"] is SolidColorBrush fill
        ? fill.Color : ThemeManager.IsDarkMode ? Color.FromRgb(24, 32, 43) : Colors.White;

    public static void Publish()
    {
        try
        {
            var resources = ThemeManager.AppResources;
            var background = BackgroundColor;
            var top = background;
            var bottom = background;
            if (ModMain.frmMain?.PanForm.Background is LinearGradientBrush scene && scene.GradientStops.Count > 0)
            {
                top = scene.GradientStops[0].Color;
                bottom = scene.GradientStops[^1].Color;
            }
            string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var json = JsonSerializer.Serialize(new
            {
                theme = ThemeManager.IsDarkMode ? "dark" : "light",
                background = Hex(background), top = Hex(top), bottom = Hex(bottom)
            });
            if (json == published && File.Exists(FilePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var pending = FilePath + ".tmp";
            File.WriteAllText(pending, json);
            File.Move(pending, FilePath, overwrite: true);
            published = json;
        }
        catch (Exception error)
        {
            // A palette update must never interrupt an active room or show a popup.
            System.Diagnostics.Debug.WriteLine($"MCTier palette update: {error.Message}");
        }
    }
}
