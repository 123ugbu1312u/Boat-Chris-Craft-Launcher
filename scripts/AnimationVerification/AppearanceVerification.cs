using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using BCCL;
using BCCL.Core.App;
using BCCL.Core.UI.Theme;
using static BCCL.ModAnimation;

internal static class AppearanceVerification
{
    public static void Run()
    {
        var available = ThemePalette.Available(false);
        if (available.Length < 13 || available.Any(p => p.Theme == ColorTheme.HmclBlue))
            throw new Exception("Normal palette list lost colors or included the seasonal theme.");
        if ((int)ColorTheme.SkyBlue != 0 || (int)ColorTheme.CatBlue != 1 ||
            (int)ColorTheme.DeathBlue != 2 || (int)ColorTheme.HmclBlue != 3)
            throw new Exception("Existing saved theme IDs changed.");
        foreach (var palette in ThemePalette.All)
        {
            var saved = JsonSerializer.Serialize(palette.Theme);
            var restored = JsonSerializer.Deserialize<ColorTheme>(saved);
            if (restored != palette.Theme || !ThemePalette.Available(true).Any(p => p.Theme == restored))
                throw new Exception("Saved palette no longer resolves to the same theme.");
        }
        var calculate = typeof(ThemeService).GetMethod("_CalculateColors", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var tone in new[] { ToneProfileConfig.DefaultLight, ToneProfileConfig.DefaultDark })
        {
            var accents = new HashSet<Color>();
            foreach (var palette in ThemePalette.All)
            {
                var colors = (CatColorResource[])calculate.Invoke(null, [tone, ThemePalette.GetArgs(palette.Theme)])!;
                foreach (var resource in colors)
                {
                    var values = new[] { resource.Red, resource.Green, resource.Blue, resource.Alpha };
                    if (values.Any(value => !float.IsFinite(value) || value < 0 || value > 1.001))
                        throw new Exception($"Invalid rendered color for {palette.Theme}.");
                }
                var accent = colors.Single(c => c.Suffix == "3");
                accents.Add(Color.FromScRgb(accent.Alpha, accent.Red, accent.Green, accent.Blue));
            }
            if (accents.Count != ThemePalette.All.Count) throw new Exception("Palette entries render as duplicate colors.");
        }
        foreach (var language in new[] { "zh-CN", "zh-TW", "en-US", "en-GB", "ja-JP", "fr-FR", "es-ES" })
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"/BCCL.Core;component/App/Localization/Languages/{language}.xaml", UriKind.Relative)
            };
            foreach (var palette in available.Where(p => (int)p.Theme >= 4 || p.Theme == ColorTheme.CatBlue))
                if (!dictionary.Contains(palette.LocalizationKey) || string.IsNullOrWhiteSpace(dictionary[palette.LocalizationKey]?.ToString()))
                    throw new Exception($"Missing {language} palette name: {palette.NameKey}.");
            if (language == "zh-CN" && dictionary["Setup.Ui.Theme.Color.CatBlue"].ToString() != "PCL蓝")
                throw new Exception("PCL blue label was not updated.");
        }
        CheckNativeEntrance();
        Console.WriteLine("PASS: 13 permanent palettes; saved IDs; distinct valid light/dark resources; 7 compiled translations; native entrance and cancellation.");
    }

    private static void CheckNativeEntrance()
    {
        var transition = typeof(FormMain).Assembly.GetType("BCCL.Multiplayer.Original.MCTierWindowTransition")!;
        var create = transition.GetMethod("Create")!;
        foreach (var fps in new[] { 60, 120, 144 })
        {
            aniGroups.Clear();
            var progress = -1d;
            var updates = new List<double>();
            Action<double> update = value => { progress = value; updates.Add(value); };
            var sequence = (AniData[])create.Invoke(null, [update])!;
            if (progress != 0) throw new Exception("Native surface was shown before its initial transition position.");
            AniStart(sequence, "native-test");
            aniGroups["native-test"].startTick = 0;
            var elapsed = 0;
            var step = Math.Max(1, (int)Math.Round(1000d / fps));
            while (elapsed < 270)
            {
                AniTimer(step);
                elapsed += step;
                if (elapsed <= 120 && (progress <= 0 || progress >= 1))
                    throw new Exception($"Native entrance snapped rather than transitioning: fps={fps}, elapsed={elapsed}, progress={progress}, updates={updates.Count}.");
            }
            if (progress != 1 || !updates.SequenceEqual(updates.Order()) || aniGroups.ContainsKey("native-test"))
                throw new Exception("Native entrance did not settle or retained an animation callback.");
            // A fast page switch must cancel the old callbacks before another entry.
            sequence = (AniData[])create.Invoke(null, [update])!;
            AniStart(sequence, "native-test");
            aniGroups["native-test"].startTick = 0;
            AniTimer(40);
            AniStop("native-test");
            var cancelledAt = progress;
            AniTimer(300);
            if (progress != cancelledAt) throw new Exception("Hidden native page continued to animate.");
        }
    }
}
