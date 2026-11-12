using System.Windows.Media;

namespace BCCL.Core.UI.Controls.SvgIcon;

internal readonly record struct SvgIconPaintOptions(
    Brush IconBrush,
    double StrokeThickness,
    bool UseOriginalColor);