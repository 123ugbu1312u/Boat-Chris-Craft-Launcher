using System.Windows;
using System.Windows.Media;

namespace BCCL;

public static partial class ModAnimation
{
    /// <summary>Reveal a centered dialog by randomly settling inward or expanding outward.</summary>
    public static AniData[] CreatePopupEnterAnimation(FrameworkElement dialog, ScaleTransform scale,
        bool? fromOutside = null)
    {
        var start = (fromOutside ?? Random.Shared.Next(2) == 0) ? 1.08d : 0.92d;
        dialog.Opacity = 0d;
        dialog.RenderTransformOrigin = new Point(0.5d, 0.5d);
        scale.ScaleX = scale.ScaleY = start;
        return
        [
            AaOpacity(dialog, 1d, 180, ease: new AniEaseOutFluent(AniEasePower.Weak)),
            AaDouble(delta =>
            {
                scale.ScaleX += (double)delta;
                scale.ScaleY = scale.ScaleX;
            },
                1d - start, 240, ease: new AniEaseOutFluent()),
            AaCode(() =>
            {
                dialog.Opacity = 1d;
                scale.ScaleX = scale.ScaleY = 1d;
            }, after: true)
        ];
    }
}
