using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BCCL;

internal static class GlassInteractionVerification
{
    private static readonly FieldInfo HostField = typeof(LiquidGlassSurface)
        .GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void Run()
    {
        var resources = System.Windows.Application.Current.Resources;
        resources["ColorBrushSemiTransparent"] = Brushes.Transparent;
        resources["ColorBrush1"] = Brushes.Black;
        resources["GlassStrength"] = 0.6d;
        resources["LiquidGlassVisibility"] = Visibility.Visible;
        resources["GlassButtonVisibility"] = Visibility.Visible;

        // Page transitions disable hit testing before a retained page is loaded again.
        // Glass must keep its own owner while that inherited flag is false.
        var page = new StackPanel { IsHitTestVisible = false };
        var root = new Border { Child = page };
        var button = new MyButton { Width = 160, Height = 40 };
        var sibling = new MyButton { Width = 160, Height = 40 };
        var radio = new MyRadioButton();
        var card = new MyCard();
        page.Children.Add(button);
        page.Children.Add(sibling);
        page.Children.Add(radio);
        page.Children.Add(card);
        root.Measure(new Size(400, 400));
        root.Arrange(new Rect(0, 0, 400, 400));

        foreach (var owner in new FrameworkElement[] { button, sibling, radio, card })
        {
            var surface = Surfaces(owner).Single();
            surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            AssertHost(surface, owner);
            page.IsHitTestVisible = true;
            AssertHost(surface, owner);
            surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            AssertHost(surface, null);
            page.IsHitTestVisible = false;
            surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            AssertHost(surface, owner);
            surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }

        // Without an explicit owner, stay with the immediate parent as well.
        var decoration = new LiquidGlassSurface();
        var wrapper = new Border { IsHitTestVisible = false, Child = decoration };
        page.Children.Add(wrapper);
        decoration.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        AssertHost(decoration, wrapper);
        decoration.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        BCCL.ModAnimation.aniGroups.Clear();
        Console.WriteLine("PASS: glass interaction owners remain isolated through disabled hit testing, unload and reload.");
    }

    private static void AssertHost(LiquidGlassSurface surface, FrameworkElement? expected)
    {
        var actual = HostField.GetValue(surface);
        if (!ReferenceEquals(actual, expected))
            throw new Exception($"Glass interaction escaped its owner: expected {expected?.GetType().Name ?? "null"}, got {actual?.GetType().Name ?? "null"}.");
    }

    private static IEnumerable<LiquidGlassSurface> Surfaces(DependencyObject node)
    {
        if (node is LiquidGlassSurface surface) yield return surface;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Surfaces(VisualTreeHelper.GetChild(node, i)))
                yield return child;
    }
}
