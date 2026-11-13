using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BCCL;
using static BCCL.ModAnimation;

internal static class Program
{
    private static int checks;

    [STAThread]
    private static void Main()
    {
        // Register definitions only: exercise production animation code without
        // starting launcher services, reading accounts or changing user settings.
        var config = typeof(BCCL.Core.App.Configuration.ConfigService);
        const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        config.GetMethod("_InitializeConfigItems", hidden)!.Invoke(null, null);
        config.GetField("_isConfigItemsInitialized", hidden)!.SetValue(null, true);
        var app = new System.Windows.Application();
        var panel = new Border { Opacity = 0.2, Background = Brushes.Black, Width = 100, Height = 80 };
        CheckAnimation(AaOpacity(panel, 0.8, 100), () => Equal(0.6, panel.Opacity), () => Equal(1, panel.Opacity));
        var shadow = new DropShadowEffect { Opacity = 0.2 };
        CheckAnimation(AaOpacity(shadow, 0.8, 100), () => Equal(0.6, shadow.Opacity), () => Equal(1, shadow.Opacity));
        var brush = new SolidColorBrush(Colors.White) { Opacity = 0.2 };
        CheckAnimation(AaOpacity(brush, 0.8, 100), () => Equal(0.6, brush.Opacity), () => Equal(1, brush.Opacity));
        var custom = new CustomOpacity { Opacity = 0.2 };
        CheckAnimation(AaOpacity(custom, 0.8, 100), () => Equal(0.6, custom.Opacity), () => Equal(1, custom.Opacity));

        CheckAnimation(AaTranslateX(panel, 30, 100),
            () => Equal(15, ((TranslateTransform)panel.RenderTransform).X),
            () => Equal(30, ((TranslateTransform)panel.RenderTransform).X));
        CheckAnimation(AaTranslateY(panel, -20, 100),
            () => Equal(-10, ((TranslateTransform)panel.RenderTransform).Y),
            () => Equal(-20, ((TranslateTransform)panel.RenderTransform).Y));
        CheckAnimation(AaDouble(shadow, DropShadowEffect.BlurRadiusProperty, 20, 100),
            () => Equal(15, shadow.BlurRadius), () => Equal(25, shadow.BlurRadius));
        var column = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        CheckAnimation(AaGridLengthWidth(column, 2, 100),
            () => Equal(2, column.Width.Value), () => Equal(3, column.Width.Value));
        CheckAnimation(AaScale(panel, 20, 100, absolute: true),
            () => { Equal(110, panel.Width); Equal(-5, panel.Margin.Left); },
            () => { Equal(120, panel.Width); Equal(100, panel.Height); Equal(-10, panel.Margin.Left); });

        app.Resources["AnimationTestBrush"] = new SolidColorBrush(Colors.Red);
        CheckAnimation(AaColor(panel, Border.BackgroundProperty, "AnimationTestBrush", 100),
            () => Equal(128, ((SolidColorBrush)panel.Background).Color.R, 1),
            () => Equal(255, ((SolidColorBrush)panel.Background).Color.R));
        if (!DependencyPropertyHelper.GetValueSource(panel, Border.BackgroundProperty).IsExpression ||
            !ReferenceEquals(app.Resources["AnimationTestBrush"], panel.Background))
            throw new Exception("Color animation did not restore the dynamic resource.");
        checks++;

        var text = new TextBlock { Text = "abcd" };
        var reveal = AaTextAppear(text, timePerText: false, time: 100);
        Add(reveal);
        AniTimer(100);
        if (text.Text != "abcd") throw new Exception("Text reveal did not finish.");
        checks++;

        var sequence = 0;
        AniStart(new[] { AaCode(() => sequence = 1, delay: 50), AaCode(() => sequence = 2, after: true) }, "verify");
        // Factory start ticks are wall-clock based; reset only the test group to the synthetic test clock.
        aniGroups["verify"].startTick = 0;
        AniTimer(50);
        Equal(0, sequence);
        AniTimer(1);
        Equal(1, sequence);
        AniTimer(1);
        Equal(2, sequence);
        if (!aniGroups.IsEmpty) throw new Exception("Completed animations remained registered.");

        var replacement = new AniGroupEntry { data = [AaCode(() => { }, delay: 1000)], startTick = long.MaxValue };
        Add(AaCode(() => aniGroups["verify"] = replacement));
        AniTimer(1);
        if (!ReferenceEquals(replacement, aniGroups["verify"]))
            throw new Exception("Completion removed a replacement animation group.");
        aniGroups.Clear();
        checks++;
        CheckPageTransitions();
        CheckPopupTransitions();
        CheckNavigationLayout();
        Console.WriteLine($"PASS: {checks} animation checks; opacity/shadow/brush/custom target, transforms, properties, scale, color resource, text and sequence.");
        if (Environment.GetCommandLineArgs().Contains("--measure")) MeasureAnimations();
        app.Shutdown();
    }

    private static void CheckNavigationLayout()
    {
        var page = new LayoutCounter();
        var viewport = new Border { Child = page, ClipToBounds = true, UseLayoutRounding = false };
        void Layout(double width, double height = 520)
        {
            viewport.Measure(new Size(width, height));
            viewport.Arrange(new Rect(0, 0, width, height));
        }
        var interpolate = typeof(FormMain).GetMethod("GetNavigationFrameWidth",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        double Width(double from, double to, double t) => (double)interpolate.Invoke(null, new object[] { from, to, t })!;
        foreach (var fps in new[] { 60, 120, 144 })
        {
            foreach (var expanding in new[] { true, false })
            {
                var from = expanding ? 76d : 140d;
                var to = expanding ? 140d : 76d;
                var frames = (int)Math.Ceiling(fps * 0.28);
                Layout(1020 - from);
                for (var frame = 1; frame <= frames; frame++)
                {
                    var width = Width(from, to, (double)frame / frames);
                    Layout(1020 - width);
                    Equal(1020 - width, page.RenderSize.Width);
                    Equal(viewport.RenderSize.Width, page.RenderSize.Width);
                }
                Equal(to, Width(from, to, 1));
                Equal(to, Width(from, to, 1.5));
            }
        }
        if (!viewport.ClipToBounds) throw new Exception("Moving page escaped the navigation viewport");
        var reversingWidth = Width(76, 140, 0.45);
        Equal(reversingWidth, Width(reversingWidth, 76, 0));
        Equal(76, Width(reversingWidth, 76, 1));
        Layout(760, 600);
        Equal(760, page.RenderSize.Width);
        Equal(600, page.RenderSize.Height);
        checks += 6;
        Console.WriteLine("PASS: continuous page layout at 60/120/144 Hz samples, reversal continuity, exact endpoints, clipping and resize.");
    }

    private sealed class LayoutCounter : FrameworkElement
    {
        public int Measures;
        public int Arranges;
        protected override Size MeasureOverride(Size availableSize) { Measures++; return availableSize; }
        protected override Size ArrangeOverride(Size finalSize) { Arranges++; return finalSize; }
    }

    private static void CheckPopupTransitions()
    {
        foreach (var fromOutside in new[] { true, false })
        {
            var dialog = new Grid();
            var scale = new ScaleTransform();
            dialog.RenderTransform = new TransformGroup
            {
                Children = { scale, new RotateTransform(), new TranslateTransform() }
            };
            var animations = CreatePopupEnterAnimation(dialog, scale, fromOutside);
            var start = scale.ScaleX;
            Equal(fromOutside ? 1.08 : 0.92, start);
            Equal(0, dialog.Opacity);
            Equal(0.5, dialog.RenderTransformOrigin.X);
            Equal(0.5, dialog.RenderTransformOrigin.Y);
            AniStart(animations, "popup-verify");
            aniGroups["popup-verify"].startTick = 0;
            AniTimer(120);
            if (fromOutside ? scale.ScaleX <= 1 || scale.ScaleX >= start
                            : scale.ScaleX >= 1 || scale.ScaleX <= start)
                throw new Exception("Dialog entrance moved in the wrong direction or overshot.");
            Equal(scale.ScaleX, scale.ScaleY);
            AdvancePageAnimations();
            Equal(1, dialog.Opacity);
            Equal(1, scale.ScaleX);
            Equal(1, scale.ScaleY);
            if (!aniGroups.IsEmpty) throw new Exception("Dialog entrance failed to settle.");
            checks++;
        }
        // Exercise random selection without requiring either random outcome.
        for (var i = 0; i < 16; i++)
        {
            var scale = new ScaleTransform();
            CreatePopupEnterAnimation(new Grid(), scale);
            if (scale.ScaleX != 0.92d && scale.ScaleX != 1.08d)
                throw new Exception("Random dialog entrance chose an unsupported scale.");
            checks++;
        }
    }

    private static void CheckPageTransitions()
    {
        // Long menus must finish as promptly as short ones; interrupted pages
        // must be reusable without retaining hidden content or a stale lifecycle.
        foreach (var count in new[] { 8, 80 })
        {
            var menu = new StackPanel();
            var content = new StackPanel();
            for (var i = 0; i < count; i++)
            {
                menu.Children.Add(new TextBlock { Text = $"Menu {i}" });
                content.Children.Add(new TextBlock { Text = $"Card {i}" });
            }
            var left = new MyPageLeft { AnimatedControl = menu };
            left.Children.Add(menu);
            var right = new MyPageRight { Child = content };
            left.TriggerShowAnimation();
            right.PageOnEnter();
            AdvancePageAnimations();
            AssertEntered();

            left.TriggerHideAnimation();
            right.PageOnExit();
            foreach (var group in aniGroups.Values) group.startTick = 0;
            AniTimer(32);
            right.PageOnForceExit();
            left.TriggerShowAnimation();
            right.PageOnEnter();
            AdvancePageAnimations();
            AssertEntered();

            void AssertEntered()
            {
                if (right.PageState != MyPageRight.PageStates.ContentStay || content.Visibility != Visibility.Visible)
                    throw new Exception("Page transition did not settle into visible content.");
                foreach (TextBlock item in menu.Children)
                {
                    Equal(0.6, item.Opacity);
                    Equal(0, ((TranslateTransform)item.RenderTransform).Y);
                }
                foreach (TextBlock item in content.Children)
                {
                    Equal(1, item.Opacity);
                    Equal(0, ((TranslateTransform)item.RenderTransform).Y);
                    if (!item.IsHitTestVisible) throw new Exception("Reentered content remained unclickable.");
                }
                if (!aniGroups.IsEmpty) throw new Exception("Page entry exceeded the bounded transition duration.");
                checks++;
            }
        }
    }

    private static void AdvancePageAnimations()
    {
        for (var frame = 0; frame < 21; frame++)
        {
            foreach (var group in aniGroups.Values) group.startTick = 0;
            AniTimer(16);
        }
    }

    private static void Add(AniData animation)
    {
        aniGroups.Clear();
        aniGroups["verify"] = new AniGroupEntry { data = [animation], startTick = 0 };
    }

    private static void CheckAnimation(AniData animation, Action halfway, Action finished)
    {
        Add(animation);
        AniTimer(50);
        halfway();
        AniTimer(50);
        finished();
        if (!aniGroups.IsEmpty) throw new Exception("Animation was not removed after completion.");
    }

    private static void Equal(double expected, double actual, double tolerance = 0.00001)
    {
        if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}; got {actual}.");
        checks++;
    }

    private static void MeasureAnimations()
    {
        const int groups = 128, frames = 300;
        for (var i = 0; i < groups; i++)
            aniGroups[i.ToString()] = new AniGroupEntry
            {
                data = [AaOpacity(new Border { Opacity = 0 }, 1, 1_000_000)], startTick = 0
            };
        for (var i = 0; i < 20; i++) AniTimer(1);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < frames; i++) AniTimer(1);
        watch.Stop();
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Console.WriteLine($"BENCHMARK: groups={groups}; frames={frames}; allocatedBytes={bytes}; elapsedMs={watch.Elapsed.TotalMilliseconds:F2}");
        aniGroups.Clear();
    }
}

public sealed class CustomOpacity { public double Opacity { get; set; } }
