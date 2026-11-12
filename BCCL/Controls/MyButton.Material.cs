using System.Windows;

namespace BCCL;

public partial class MyButton
{
    public static readonly DependencyProperty GlassMaterialEnabledProperty = DependencyProperty.Register(
        nameof(GlassMaterialEnabled), typeof(bool), typeof(MyButton),
        new FrameworkPropertyMetadata(false, (owner, _) => ((MyButton)owner).UpdateButtonMaterial()));
    public static readonly DependencyProperty CapsuleShapeEnabledProperty = DependencyProperty.Register(
        nameof(CapsuleShapeEnabled), typeof(bool), typeof(MyButton),
        new FrameworkPropertyMetadata(false, (owner, _) => owner.CoerceValue(CornerRadiusProperty)));

    public bool GlassMaterialEnabled => (bool)GetValue(GlassMaterialEnabledProperty);
    public bool CapsuleShapeEnabled => (bool)GetValue(CapsuleShapeEnabledProperty);

    static MyButton()
    {
        // Coercion preserves each caller's original radius (including a resource binding),
        // so changing back to the original style never destroys a local customization.
        CornerRadiusProperty.OverrideMetadata(typeof(MyButton), new FrameworkPropertyMetadata(
            new CornerRadius(3), null, (owner, original) =>
            {
                var button = (MyButton)owner;
                return button.CapsuleShapeEnabled && button.ActualHeight > 0 && button.ActualWidth > 0
                    ? new CornerRadius(Math.Min(button.ActualWidth, button.ActualHeight) / 2)
                    : original;
            }));
    }

    private void InitializeButtonMaterial()
    {
        SetResourceReference(GlassMaterialEnabledProperty, "GlassButtonsEnabled");
        SetResourceReference(CapsuleShapeEnabledProperty, "CapsuleButtonsEnabled");
        SizeChanged += (_, _) => CoerceValue(CornerRadiusProperty);
        Loaded += (_, _) => UpdateButtonMaterial();
        UpdateButtonMaterial();
    }

    private string ButtonBackgroundResource(bool hover)
    {
        if (GlassMaterialEnabled)
            return hover ? ColorType == ColorState.Red ? "GlassButtonRedHoverFill" : "GlassButtonHoverFill" : "GlassButtonFill";
        return hover ? ColorType == ColorState.Red ? "ColorBrushRedBack" : "ColorBrush7" : "ColorBrushHalfWhite";
    }

    private void UpdateButtonMaterial()
    {
        if (PanFore is null) return;
        ModAnimation.AniStop("MyButton Background " + Uuid);
        PanFore.SetResourceReference(BackgroundProperty, ButtonBackgroundResource(IsMouseOver));
    }
}
