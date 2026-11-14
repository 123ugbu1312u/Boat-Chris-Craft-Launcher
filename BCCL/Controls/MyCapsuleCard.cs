using System.Windows;
using System.Windows.Media;

namespace BCCL;

/// <summary>A capsule action strip whose visual and input bounds share one outline.</summary>
public sealed class MyCapsuleCard : MyCard
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var size = sizeInfo.NewSize;
        var radius = Math.Min(size.Width, size.Height) / 2;
        CornerRadius = new CornerRadius(radius);
        var outline = new RectangleGeometry(new Rect(size), radius, radius);
        outline.Freeze();
        // Visual clipping also clips hit testing for descendants, including the
        // icon buttons' rectangular backgrounds at both ends of the action strip.
        Clip = outline;
    }
}
