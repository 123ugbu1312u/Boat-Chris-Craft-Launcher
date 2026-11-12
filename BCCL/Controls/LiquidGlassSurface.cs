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
    private static readonly Pen PulsePen = CreatePulsePen();
    private static Pen CreatePulsePen() { var pen = new Pen(Brushes.White, 1.5); pen.Freeze(); return pen; }
    private void EnsureMaterials(double s, Brush? scene)
    {
        if (materialStrength == s && ReferenceEquals(sceneSource, scene)) return;
        materialStrength = s;
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
        prismBrush = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(1,1) };
        prismBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(180*s),255,255,255),0));
        prismBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(60*s),150,218,255),0.3));
        prismBrush.GradientStops.Add(new GradientStop(Colors.Transparent,0.5));
        prismBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(90*s),186,168,246),0.8));
        prismBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(160*s),255,255,255),1));
        prismBrush.Freeze();
        glintBrush = new RadialGradientBrush(Color.FromArgb((byte)(220*s),255,255,255),Colors.Transparent) { RadiusX=0.5, RadiusY=0.7 };
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
        // Existing card decorations still find their interactive parent as before.
        while (host is not null && !host.IsHitTestVisible) host = VisualTreeHelper.GetParent(host) as FrameworkElement;
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
        if (!IsVisible || !IsEnabled || Strength <= 0 || Environment.TickCount64 - lastMove < 33) return;
        lastMove = Environment.TickCount64;
        var p = e.GetPosition(this);
        var next = new Point(Math.Clamp(p.X / Math.Max(1, ActualWidth), 0, 1), Math.Clamp(p.Y / Math.Max(1, ActualHeight), 0, 1));
        if (next == pointer) return;
        pointer = next;
        InvalidateVisual();
    }
    private void Leave(object sender, MouseEventArgs e) { if (!IsVisible) return; pointer = new Point(0.25, 0.1); InvalidateVisual(); }
    private void Press(object sender, MouseButtonEventArgs e)
    {
        if (!IsVisible || !IsEnabled || Strength <= 0 || e.ChangedButton != MouseButton.Left) return;
        var p = e.GetPosition(this);
        pointer = new Point(Math.Clamp(p.X / Math.Max(1, ActualWidth), 0, 1), Math.Clamp(p.Y / Math.Max(1, ActualHeight), 0, 1));
        BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { FillBehavior = FillBehavior.Stop });
    }
    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Clamp(Strength, 0, 1);
        if (s <= 0 || ActualWidth < 4 || ActualHeight < 4) return;
        var rect = new Rect(0.75, 0.75, ActualWidth - 1.5, ActualHeight - 1.5);
        outline ??= new RectangleGeometry(rect, Radius, Radius);
        if (rim is null) {
            var inset = Math.Min(8, Math.Min(rect.Width, rect.Height) / 3);
            var inner = rect; inner.Inflate(-inset, -inset);
            rim = Geometry.Combine(outline, new RectangleGeometry(inner, Math.Max(0, Radius - inset), Math.Max(0, Radius - inset)), GeometryCombineMode.Exclude, null);
            rim.Freeze();
        }
        dc.PushClip(outline);
        // Reuse the exact same optical material during pointer motion and pulse frames.
        EnsureMaterials(s, ModMain.frmMain?.PanForm?.Background);
        if (refractedScene is not null) {
            refractionScale!.CenterX = pointer.X;
            refractionScale.CenterY = pointer.Y;
            refractionOffset!.X = (pointer.X - 0.5) * s * 0.12;
            refractionOffset.Y = (pointer.Y - 0.5) * s * 0.1;
            dc.PushOpacity(0.5 * s); dc.DrawGeometry(refractedScene, null, rim); dc.Pop();
        }
        dc.DrawGeometry(prismBrush, null, rim);
        glintBrush!.Center = glintBrush.GradientOrigin = pointer;
        dc.DrawGeometry(null,glintPen,outline);
        if (Pulse < 1) {
            dc.PushOpacity((1-Pulse)*s*0.18);
            dc.DrawEllipse(Brushes.White, PulsePen,new Point(pointer.X*ActualWidth,pointer.Y*ActualHeight),Pulse*ActualWidth,Pulse*ActualWidth);
            dc.Pop();
        }
        dc.Pop();
    }
}
