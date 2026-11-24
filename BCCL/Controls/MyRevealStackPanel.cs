using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BCCL;

/// <summary>Reveals navigation rows without resizing their text and icons.</summary>
public sealed class MyRevealStackPanel : Panel
{
    public static readonly DependencyProperty RevealProgressProperty = DependencyProperty.RegisterAttached(
        "RevealProgress", typeof(double), typeof(MyRevealStackPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsParentMeasure |
            FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetRevealProgress(DependencyObject element) =>
        Math.Clamp((double)element.GetValue(RevealProgressProperty), 0, 1);

    public static void SetRevealProgress(DependencyObject element, double value) =>
        element.SetValue(RevealProgressProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (UIElement child in InternalChildren)
        {
            // Always measure the full row. Only its allocation in this panel changes,
            // so the 40px MyListItem layout breakpoint never runs during a reveal.
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height * GetRevealProgress(child);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = 0;
        foreach (UIElement child in InternalChildren)
        {
            var progress = GetRevealProgress(child);
            var fullHeight = child.DesiredSize.Height;
            var visibleHeight = fullHeight * progress;
            var offset = fullHeight * (1 - progress);
            child.Arrange(new Rect(0, top - offset, finalSize.Width, fullHeight));
            // Clip to the allocated slot: the moving row cannot paint over either
            // its header or the next section, including when interrupted/reversed.
            var bounds = new Rect(0, offset, finalSize.Width, visibleHeight);
            if (child.Clip is RectangleGeometry clip && !clip.IsFrozen)
                clip.Rect = bounds;
            else
                child.Clip = new RectangleGeometry(bounds);
            top += visibleHeight;
        }
        return finalSize;
    }
}
