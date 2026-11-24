using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BCCL;

// Optical decoration stays below content: only the rim samples/distorts the scene brush.
public sealed class LiquidGlassSurface : FrameworkElement
{
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(nameof(Strength), typeof(double), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty PulseProperty = DependencyProperty.Register(nameof(Pulse), typeof(double), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    public double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    public static readonly DependencyProperty RimVisibilityProperty = DependencyProperty.Register(
        nameof(RimVisibility), typeof(Visibility), typeof(LiquidGlassSurface),
        new FrameworkPropertyMetadata(Visibility.Visible, FrameworkPropertyMetadataOptions.AffectsRender));
    public Visibility RimVisibility { get => (Visibility)GetValue(RimVisibilityProperty); set => SetValue(RimVisibilityProperty, value); }
    public static readonly DependencyProperty RimTintProperty = DependencyProperty.Register(
        nameof(RimTint), typeof(Color), typeof(LiquidGlassSurface),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public Color RimTint { get => (Color)GetValue(RimTintProperty); set => SetValue(RimTintProperty, value); }
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(LiquidGlassSurface),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsRender, (owner, _) =>
        {
            var surface = (LiquidGlassSurface)owner;
            surface.rim = null; surface.outline = null;
        }));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public static readonly DependencyProperty InteractionHostProperty = DependencyProperty.Register(nameof(InteractionHost), typeof(FrameworkElement), typeof(LiquidGlassSurface),
        new PropertyMetadata(null, (owner, _) => { var surface = (LiquidGlassSurface)owner; if (surface.IsLoaded) surface.AttachHost(); }));
    public FrameworkElement? InteractionHost { get => (FrameworkElement?)GetValue(InteractionHostProperty); set => SetValue(InteractionHostProperty, value); }
    private FrameworkElement? host;
    private Geometry? rim;
    private RectangleGeometry? outline;
    private Point pointer = new(0.25, 0.1);
    private long lastMove;
    private Brush? sceneSource, refractedScene;
    private ScaleTransform? refractionScale;
    private TranslateTransform? refractionOffset;
    private LinearGradientBrush? prismBrush;
    private RadialGradientBrush? glintBrush;
    private Pen? glintPen;
    private double materialStrength = -1;
    private Color materialTint;
    private static readonly Pen PulsePen = CreatePulsePen();
    private static Pen CreatePulsePen() { var pen = new Pen(Brushes.White, 1.5); pen.Freeze(); return pen; }
    private void EnsureMaterials(double s, Brush? scene)
    {
        var tint = RimTint;
        if (materialStrength == s && materialTint == tint && ReferenceEquals(sceneSource, scene)) return;
        materialStrength = s;
        materialTint = tint;
        sceneSource = scene;
        refractionScale = new ScaleTransform(1 + s * 0.22, 1 + s * 0.18);
        refractionOffset = new TranslateTransform();
        if (scene is not null) {
            refractedScene = scene.CloneCurrentValue();
            var transforms = new TransformGroup();
            transforms.Children.Add(refractionScale);
            transforms.Children.Add(refractionOffset);
            refractedScene.RelativeTransform = transforms;
        } else refractedScene = null;
        Color HighlightColor(double opacity, byte red, byte green, byte blue)
        {
            var alpha = (byte)(opacity * s);
            if (tint.A == 0) return Color.FromArgb(alpha, red, green, blue);
            return Color.FromArgb(alpha, (byte)(0.8 * tint.R + 0.2 * red),
                (byte)(0.8 * tint.G + 0.2 * green), (byte)(0.8 * tint.B + 0.2 * blue));
        }

        prismBrush = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(1,1) };
        prismBrush.GradientStops.Add(new GradientStop(HighlightColor(180, 255, 255, 255), 0));
        prismBrush.GradientStops.Add(new GradientStop(HighlightColor(60, 150, 218, 255), 0.3));
        prismBrush.GradientStops.Add(new GradientStop(Colors.Transparent,0.5));
        prismBrush.GradientStops.Add(new GradientStop(HighlightColor(90, 186, 168, 246), 0.8));
        prismBrush.GradientStops.Add(new GradientStop(HighlightColor(160, 255, 255, 255), 1));
        prismBrush.Freeze();
        glintBrush = new RadialGradientBrush(HighlightColor(220, 255, 255, 255), Colors.Transparent) { RadiusX = 0.5, RadiusY = 0.7 };
        glintPen = new Pen(glintBrush,2.2);
    }

    public LiquidGlassSurface()
    {
        IsHitTestVisible = false;
        SetResourceReference(StrengthProperty, "GlassStrength");
        SetResourceReference(VisibilityProperty, "LiquidGlassVisibility");
        Loaded += (_, _) => AttachHost();
        Unloaded += (_, _) => {
            DetachHost();
            BeginAnimation(PulseProperty, null);
        };
        SizeChanged += (_, _) => { rim = null; outline = null; InvalidateVisual(); };
    }
    private void AttachHost()
    {
        DetachHost();
        host = InteractionHost ?? Parent as FrameworkElement;
        // Page transitions temporarily disable hit testing, including on explicit hosts.
        // Keep the same owner: climbing to a shared ancestor makes sibling surfaces
        // react to every click on the page, even after hit testing is restored.
        if (host is null) return;
        host.MouseMove += Move;
        host.MouseLeave += Leave;
        host.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Press), true);
    }
    private void DetachHost()
    {
        if (host is null) return;
        host.MouseMove -= Move; host.MouseLeave -= Leave;
        host.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Press));
        host = null;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (RimVisibility != Visibility.Visible || !IsVisible || !IsEnabled || Strength <= 0 ||
            host is not { IsHitTestVisible: true, IsEnabled: true } ||
            Environment.TickCount64 - lastMove < 33 || !TryGetPointer(e, out var next)) return;
        lastMove = Environment.TickCount64;
        if (next == pointer) return;
        pointer = next;
        InvalidateVisual();
    }
    private void Leave(object sender, MouseEventArgs e) { if (!IsVisible) return; pointer = new Point(0.25, 0.1); InvalidateVisual(); }
    private void Press(object sender, MouseButtonEventArgs e)
    {
        if (!IsVisible || !IsEnabled || Strength <= 0 || e.ChangedButton != MouseButton.Left ||
            host is not { IsHitTestVisible: true, IsEnabled: true } || !TryGetPointer(e, out var next)) return;
        pointer = next;
        BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { FillBehavior = FillBehavior.Stop });
    }
    private bool TryGetPointer(MouseEventArgs e, out Point position)
    {
        position = default;
        var p = e.GetPosition(this);
        // A host can be larger than its decoration, or continue receiving captured input.
        // Ignore outside coordinates instead of clamping them into an edge pulse.
        if (ActualWidth <= 0 || ActualHeight <= 0 || !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
            !new Rect(RenderSize).Contains(p)) return false;
        position = new Point(p.X / ActualWidth, p.Y / ActualHeight);
        return true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Clamp(Strength, 0, 1);
        if (s <= 0 || ActualWidth < 4 || ActualHeight < 4) return;
        var showRim = RimVisibility == Visibility.Visible;
        if (!showRim && Pulse >= 1) return;
        outline ??= new RectangleGeometry(new Rect(0.75, 0.75, ActualWidth - 1.5, ActualHeight - 1.5), Radius, Radius);

        dc.PushClip(outline);
        if (showRim)
        {
            if (rim is null)
            {
                var inner = outline.Rect;
                var inset = Math.Min(8, Math.Min(inner.Width, inner.Height) / 3);
                inner.Inflate(-inset, -inset);
                var innerRadius = Math.Max(0, Radius - inset);
                rim = Geometry.Combine(outline, new RectangleGeometry(inner, innerRadius, innerRadius), GeometryCombineMode.Exclude, null);
                rim.Freeze();
            }

            // Reuse the exact same optical material during pointer motion and pulse frames.
            EnsureMaterials(s, ModMain.frmMain?.PanForm?.Background);
            if (refractedScene is not null)
            {
                refractionScale!.CenterX = pointer.X;
                refractionScale.CenterY = pointer.Y;
                refractionOffset!.X = (pointer.X - 0.5) * s * 0.12;
                refractionOffset.Y = (pointer.Y - 0.5) * s * 0.1;
                dc.PushOpacity(0.5 * s); dc.DrawGeometry(refractedScene, null, rim); dc.Pop();
            }
            dc.DrawGeometry(prismBrush, null, rim);
            glintBrush!.Center = glintBrush.GradientOrigin = pointer;
            dc.DrawGeometry(null, glintPen, outline);
        }
        if (Pulse < 1) {
            dc.PushOpacity((1-Pulse)*s*0.18);
            dc.DrawEllipse(Brushes.White, PulsePen,new Point(pointer.X*ActualWidth,pointer.Y*ActualHeight),Pulse*ActualWidth,Pulse*ActualWidth);
            dc.Pop();
        }
        dc.Pop();
    }
}
