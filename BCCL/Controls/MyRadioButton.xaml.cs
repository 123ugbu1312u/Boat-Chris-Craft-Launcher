using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BCCL.Core.App;
using BCCL.Core.UI.Theme;

using BCCL.Core.App.Localization;
namespace BCCL;

[ContentProperty("Inlines")]
public partial class MyRadioButton
{
    public delegate void ChangeEventHandler(MyRadioButton sender, bool raiseByMouse);

    public delegate void CheckEventHandler(MyRadioButton sender, bool raiseByMouse);

    public delegate void PreviewClickEventHandler(object sender, ModBase.RouteEventArgs e);

    public enum ColorState
    {
        White,
        Highlight
    }

    // 动画

    private const int animationTimeOfMouseIn = 90; // 鼠标指向动画长度
    private const int animationTimeOfMouseOut = 150; // 鼠标移出动画长度
    private const int animationTimeOfCheck = 120; // 勾选状态变更动画长度

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string),
        typeof(MyRadioButton), new PropertyMetadata((sender, e) =>
        {
            if (sender is MyRadioButton rb && rb.LabText is not null) rb.LabText.Text = (string)e.NewValue;
        }));

    private bool _Checked; // 是否选中
    private bool _hasLegacyLogo;
    private bool isMouseDown;

    // 基础

    public int Uuid = ModBase.GetUuid();

    public MyRadioButton()
    {
        InitializeComponent();
        RefreshLogoHostVisibility();

        Loaded += (_, _) =>
        {
            LabText?.Text = (string)GetValue(TextProperty);

            ThemeService.ColorModeChanged += OnColorModeChanged;
            ThemeService.ColorThemeChanged += OnColorThemeChanged;
        };

        Unloaded += (_, _) =>
        {
            ThemeService.ColorModeChanged -= OnColorModeChanged;
            ThemeService.ColorThemeChanged -= OnColorThemeChanged;
        };

        MouseLeftButtonUp += (_, _) => Radiobox_MouseUp();
        MouseLeftButtonDown += (_, _) => Radiobox_MouseDown();
        MouseLeave += (_, _) => Radiobox_MouseLeave();
        MouseEnter += RefreshColor;
        MouseLeave += RefreshColor;
        Loaded += RefreshColor;
        KeyDown += Navigation_KeyDown;
        MouseMove += Navigation_MouseMove;
    }

    private void OnColorModeChanged(bool isDarkMode, ColorTheme theme)
    {
        Dispatcher.Invoke(RefreshMyRadioButtonColor);
    }

    private void OnColorThemeChanged(ColorTheme theme)
    {
        Dispatcher.Invoke(RefreshMyRadioButtonColor);
    }

    // 自定义属性

    public string Logo
    {
        get => ShapeLogo.Data?.ToString() ?? string.Empty;
        set
        {
            if (ShapeLogo is null) return;
            _hasLegacyLogo = !string.IsNullOrWhiteSpace(value);
            ShapeLogo.Data = _hasLegacyLogo
                ? (Geometry)new GeometryConverter().ConvertFromString(value)
                : null;
            SvgIconControlHelper.ApplyVisibility(ShapeLogo, ShapeSvgIcon, IsUsingSvgIcon);
            RefreshLogoHostVisibility();
        }
    }

    public string SvgIcon
    {
        get;
        set
        {
            value ??= string.Empty;
            if (value == field)
                return;
            field = value;
            if (ShapeLogo is null || ShapeSvgIcon is null)
                return;
            SvgIconControlHelper.ApplyIcon(ShapeLogo, ShapeSvgIcon, field);
            ApplyLogoScale();
            RefreshLogoHostVisibility();
            RefreshColor();
        }
    } = string.Empty;

    private bool IsUsingSvgIcon => SvgIconControlHelper.HasSvgIcon(SvgIcon);

    private double EffectiveLogoScale => IsUsingSvgIcon ? 1D : LogoScale;

    private bool HasAnyIcon => IsUsingSvgIcon || _hasLegacyLogo;

    public bool IsNavigationItem
    {
        get;
        set
        {
            field = value;
            RefreshLogoHostVisibility();
        }
    }

    private void ApplyLogoScale()
    {
        LogoHost?.RenderTransform = new ScaleTransform
        {
            ScaleX = EffectiveLogoScale,
            ScaleY = EffectiveLogoScale
        };
    }

    private void RefreshLogoHostVisibility()
    {
        if (LogoHost is null || LabText is null)
            return;

        if (IsNavigationItem)
        {
            PanContent.Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Focusable = true;
            ClipToBounds = true;
            MinHeight = 44;
            MaxHeight = double.PositiveInfinity;
            CornerRadius = new CornerRadius(16);
            LogoHost.Visibility = HasAnyIcon ? Visibility.Visible : Visibility.Collapsed;
            LogoHost.Width = LogoHost.Height = 22;
            LogoHost.Margin = new Thickness(15, 0, 0, 0);
            LogoHost.HorizontalAlignment = HorizontalAlignment.Center;
            LabText.Margin = new Thickness(12, 0, 14, 0);
            LabText.FontSize = 14;
            LabText.HorizontalAlignment = HorizontalAlignment.Center;
            LabText.TextAlignment = TextAlignment.Center;
            LabText.TextWrapping = TextWrapping.NoWrap;
            return;
        }

        if (HasAnyIcon)
        {
            LogoHost.Visibility = Visibility.Visible;
            LogoHost.Width = 16;
        }
        else
        {
            LogoHost.Visibility = Visibility.Visible;
            LogoHost.Width = 0;
        }

        LogoHost.Height = 16;
        LogoHost.Margin = new Thickness(12, 0, 0, 0);
        LabText.Margin = new Thickness(8, 0, 12, 0);
    }

    public double LogoScale
    {
        get;
        set
        {
            field = value;
            ApplyLogoScale();
        }
    } = 1d;

    public bool Checked
    {
        get => _Checked;
        set => SetChecked(value, false, true);
    }

    public InlineCollection Inlines => LabText.Inlines;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    } // 内容

    public ColorState ColorType
    {
        get;
        set
        {
            field = value;
            RefreshColor();
        }
    } = ColorState.White; // 颜色类别

    public event CheckEventHandler? Check;

    /// <summary>
    ///     手动设置 Checked 属性。
    /// </summary>
    /// <param name="value">新的 Checked 属性。</param>
    /// <param name="raiseByMouse">是否由用户引发。</param>
    /// <param name="anime">是否执行动画。</param>
    public void SetChecked(bool value, bool raiseByMouse, bool anime)
    {
        try
        {
            // 自定义属性基础

            var isChanged = false;
            if (_Checked != value)
            {
                _Checked = value;
                isChanged = true;
            }

            // 保证只有一个单选框选中

            if (Parent is null) return;
            var radioboxList = new List<MyRadioButton>();
            var checkedCount = 0;
            // 收集控件列表与选中个数
            foreach (var Control in ((Panel)Parent).Children)
                if (Control is MyRadioButton radioButton)
                {
                    radioboxList.Add(radioButton);
                    if (radioButton.Checked)
                        checkedCount += 1;
                }

            // 判断选中情况
            switch (checkedCount)
            {
                case 0:
                {
                    // 没有任何单选框被选中，选择第一个
                    radioboxList[0].Checked = true;
                    break;
                }
                case var @case when @case > 1:
                {
                    // 选中项目多于 1 个
                    if (Checked)
                    {
                        // 如果本控件选中，则取消其他所有控件的选中
                        foreach (var Control in radioboxList)
                            if (Control.Checked && !Control.Equals(this))
                                Control.Checked = false;
                    }
                    else
                    {
                        // 如果本控件未选中，则只保留第一个选中的控件
                        var firstChecked = false;
                        foreach (var Control in radioboxList)
                            if (Control.Checked)
                            {
                                if (firstChecked)
                                    Control.Checked = false; // 修改 Checked 会自动触发 Change 事件，所以不用额外触发
                                else
                                    firstChecked = true;
                            }
                    }

                    break;
                }
            }

            // 更改动画

            if (!isChanged)
                return;
            RefreshColor(null, anime);

            // 触发事件
            if (Checked)
                Check?.Invoke(this, raiseByMouse);
            ModMain.RaiseCustomEvent(this);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "单选按钮勾选改变错误",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.Error.OperationFailed"));
        }
    }

    // 点击事件

    public event PreviewClickEventHandler? PreviewClick;

    private void Radiobox_MouseUp()
    {
        if (Checked && !IsNavigationItem)
            return;
        if (!isMouseDown)
            return;
        ModBase.Log("[Control] 按下单选按钮：" + Text);
        isMouseDown = false;
        var e = new ModBase.RouteEventArgs(true);
        PreviewClick?.Invoke(this, e);
        if (e.handled)
            return;
        ActivateNavigationItem();
        RefreshColor();
    }

    private void Radiobox_MouseDown()
    {
        if (Checked && !IsNavigationItem)
            return;
        isMouseDown = true;
        RefreshColor();
    }

    private void Radiobox_MouseLeave()
    {
        isMouseDown = false;
    }

    private void ActivateNavigationItem()
    {
        // A selected sidebar item must still return from a nested page.
        if (IsNavigationItem && Checked)
            Check?.Invoke(this, true);
        else
            SetChecked(true, true, true);
    }

    private void Navigation_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsNavigationItem) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            ActivateNavigationItem();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End) || Parent is not Panel panel)
            return;
        var items = panel.Children.OfType<MyRadioButton>()
            .Where(item => item.IsNavigationItem && item.IsVisible && item.IsEnabled).ToList();
        if (items.Count == 0) return;
        var index = items.IndexOf(this);
        var targetIndex = e.Key switch
        {
            Key.Home => 0,
            Key.End => items.Count - 1,
            Key.Up => (index + items.Count - 1) % items.Count,
            _ => (index + 1) % items.Count
        };
        items[targetIndex].Focus();
        items[targetIndex].ActivateNavigationItem();
        e.Handled = true;
    }

    private void RefreshColor(object obj = null, object e = null)
    {
        try
        {
            if (IsNavigationItem && LiquidGlassTheme.IsEnabled)
            {
                RefreshGlassColor(!false.Equals(e));
                return;
            }
            if (IsNavigationItem)
            {
                ModAnimation.AniStop("MyRadioButton Glass " + Uuid);
                GlassSurface.Visibility = Visibility.Collapsed;
                Effect = null;
                CornerRadius = new CornerRadius(16);
                if (RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1;
                var backgroundKey = Checked || IsMouseOver ? "WorkbenchAccentFill" : "ColorBrushSemiTransparent";
                if (IsLoaded && ModAnimation.AniControlEnabled == 0)
                    ModAnimation.AniStart(ModAnimation.AaColor(this, BackgroundProperty, backgroundKey, 150),
                        "MyRadioButton Color " + Uuid);
                else
                    SetResourceReference(BackgroundProperty, backgroundKey);
                ShapeLogo.SetResourceReference(Shape.FillProperty, Checked ? "ColorBrush2" : "ColorBrushGray2");
                LabText.SetResourceReference(TextBlock.ForegroundProperty, Checked ? "ColorBrush2" : "ColorBrushGray2");
                LabText.FontWeight = Checked ? FontWeights.SemiBold : FontWeights.Normal;
                return;
            }
            if (IsLoaded && ModAnimation.AniControlEnabled == 0 &&
                !false.Equals(e)) // 防止默认属性变更触发动画，若强制不执行动画，则 e 为 False
            {
                switch (ColorType)
                {
                    case ColorState.White:
                    {
                        if (Checked)
                        {
                            // 勾选
                            var color3 = new ModBase.MyColor(ThemeManager.AppResources["ColorObject3"]);
                            ModAnimation.AniStart(
                                new[]
                                {
                                    ModAnimation.AaColor(ShapeLogo, Shape.FillProperty, color3 - ShapeLogo.Fill,
                                        animationTimeOfCheck),
                                    ModAnimation.AaColor(LabText, TextBlock.ForegroundProperty,
                                        color3 - LabText.Foreground, animationTimeOfCheck)
                                }, "MyRadioButton Checked " + Uuid);
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty,
                                    new ModBase.MyColor(255d, 255d, 255d) - Background, animationTimeOfCheck),
                                "MyRadioButton Color " + Uuid);
                        }
                        else if (isMouseDown)
                        {
                            // 按下
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty,
                                    new ModBase.MyColor(120d,
                                        new ModBase.MyColor(ThemeManager.AppResources["ColorObject8"])) - Background, 60),
                                "MyRadioButton Color " + Uuid);
                        }
                        else if (IsMouseOver)
                        {
                            // 指向
                            ModAnimation.AniStart(
                                new[]
                                {
                                    ModAnimation.AaColor(ShapeLogo, Shape.FillProperty,
                                        new ModBase.MyColor(255d, 255d, 255d) - ShapeLogo.Fill, animationTimeOfMouseIn),
                                    ModAnimation.AaColor(LabText, TextBlock.ForegroundProperty,
                                        new ModBase.MyColor(255d, 255d, 255d) - LabText.Foreground,
                                        animationTimeOfMouseIn)
                                }, "MyRadioButton Checked " + Uuid);
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty,
                                    new ModBase.MyColor(50d,
                                        new ModBase.MyColor(ThemeManager.AppResources["ColorObject8"])) - Background,
                                    animationTimeOfMouseIn), "MyRadioButton Color " + Uuid);
                        }
                        else
                        {
                            // 正常
                            ModAnimation.AniStart(
                                new[]
                                {
                                    ModAnimation.AaColor(ShapeLogo, Shape.FillProperty,
                                        new ModBase.MyColor(255d, 255d, 255d) - ShapeLogo.Fill,
                                        animationTimeOfMouseOut),
                                    ModAnimation.AaColor(LabText, TextBlock.ForegroundProperty,
                                        new ModBase.MyColor(255d, 255d, 255d) - LabText.Foreground,
                                        animationTimeOfMouseOut)
                                }, "MyRadioButton Checked " + Uuid);
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty,
                                    new ModBase.MyColor(ThemeManager.AppResources["ColorBrushSemiTransparent"]) -
                                    Background, animationTimeOfMouseOut), "MyRadioButton Color " + Uuid);
                        }

                        break;
                    }
                    case ColorState.Highlight:
                    {
                        if (Checked)
                        {
                            // 勾选
                            ModAnimation.AniStart(
                                new[]
                                {
                                    ModAnimation.AaColor(ShapeLogo, Shape.FillProperty,
                                        new ModBase.MyColor(255d, 255d, 255d) - ShapeLogo.Fill, animationTimeOfCheck),
                                    ModAnimation.AaColor(LabText, TextBlock.ForegroundProperty,
                                        new ModBase.MyColor(255d, 255d, 255d) - LabText.Foreground,
                                        animationTimeOfCheck)
                                }, "MyRadioButton Checked " + Uuid);
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty, "ColorBrush3", animationTimeOfCheck),
                                "MyRadioButton Color " + Uuid);
                        }
                        else if (isMouseDown)
                        {
                            // 按下
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty, "ColorBrush6", animationTimeOfMouseIn),
                                "MyRadioButton Color " + Uuid);
                        }
                        else if (IsMouseOver)
                        {
                            // 指向
                            ModAnimation.AniStart(
                                new[]
                                {
                                    ModAnimation.AaColor(ShapeLogo, Shape.FillProperty, "ColorBrush3",
                                        animationTimeOfMouseIn),
                                    ModAnimation.AaColor(LabText, TextBlock.ForegroundProperty, "ColorBrush3",
                                        animationTimeOfMouseIn)
                                }, "MyRadioButton Checked " + Uuid);
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty, "ColorBrush7", animationTimeOfMouseIn),
                                "MyRadioButton Color " + Uuid);
                        }
                        else
                        {
                            // 正常
                            ModAnimation.AniStart(
                                new[]
                                {
                                    ModAnimation.AaColor(ShapeLogo, Shape.FillProperty, "ColorBrush3",
                                        animationTimeOfMouseOut),
                                    ModAnimation.AaColor(LabText, TextBlock.ForegroundProperty, "ColorBrush3",
                                        animationTimeOfMouseOut)
                                }, "MyRadioButton Checked " + Uuid);
                            ModAnimation.AniStart(
                                ModAnimation.AaColor(this, BackgroundProperty,
                                    new ModBase.MyColor(ThemeManager.AppResources["ColorBrushSemiTransparent"]) -
                                    Background, animationTimeOfMouseOut), "MyRadioButton Color " + Uuid);
                        }

                        break;
                    }
                }
            }

            else
            {
                // 不使用动画
                ModAnimation.AniStop("MyRadioButton Checked " + Uuid);
                ModAnimation.AniStop("MyRadioButton Color " + Uuid);
                switch (ColorType)
                {
                    case ColorState.White:
                    {
                        if (Checked)
                        {
                            Background = new ModBase.MyColor(255d, 255d, 255d);
                            ShapeLogo.SetResourceReference(Shape.FillProperty, "ColorBrush3");
                            LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
                        }
                        else
                        {
                            Background = (Brush)ThemeManager.AppResources["ColorBrushSemiTransparent"];
                            ShapeLogo.Fill = new ModBase.MyColor(255d, 255d, 255d);
                            LabText.Foreground = new ModBase.MyColor(255d, 255d, 255d);
                        }

                        break;
                    }
                    case ColorState.Highlight:
                    {
                        if (Checked)
                        {
                            SetResourceReference(BackgroundProperty, "ColorBrush3");
                            ShapeLogo.Fill = new ModBase.MyColor(255d, 255d, 255d);
                            LabText.Foreground = new ModBase.MyColor(255d, 255d, 255d);
                        }
                        else
                        {
                            Background = (Brush)ThemeManager.AppResources["ColorBrushSemiTransparent"];
                            ShapeLogo.SetResourceReference(Shape.FillProperty, "ColorBrush3");
                            LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "刷新单选按钮颜色出错");
        }
    }

    public void RefreshMyRadioButtonColor()
    {
        RefreshColor();
    }

    private RadialGradientBrush? _glassHighlight;
    private long _lastGlassHighlightTick;

    private void Navigation_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsNavigationItem || !LiquidGlassTheme.IsEnabled || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var now = Environment.TickCount64;
        if (now - _lastGlassHighlightTick < 33) return;
        _lastGlassHighlightTick = now;
        _glassHighlight ??= (RadialGradientBrush)GlassSheen.Background.Clone();
        GlassSheen.Background = _glassHighlight;
        var point = e.GetPosition(this);
        var relative = new Point(Math.Clamp(point.X / ActualWidth, 0, 1), Math.Clamp(point.Y / ActualHeight, 0, 1));
        if ((relative - _glassHighlight.Center).LengthSquared < 0.0004) return;
        _glassHighlight.Center = _glassHighlight.GradientOrigin = relative;
    }

    private void RefreshGlassColor(bool allowAnimation)
    {
        ModAnimation.AniStop("MyRadioButton Checked " + Uuid);
        GlassSurface.Visibility = Visibility.Visible;
        CornerRadius = new CornerRadius(20);
        RenderTransformOrigin = new Point(0.5, 0.5);
        var color = ((SolidColorBrush)ThemeManager.AppResources["ColorBrushBackground"]).Color;
        var alpha = Checked ? (byte)160 : IsMouseOver ? (byte)90 : (byte)24;
        var target = new ModBase.MyColor(Color.FromArgb(alpha, color.R, color.G, color.B));
        var opacity = (Checked ? 1d : IsMouseOver ? 0.85d : 0.28d) * LiquidGlassTheme.EffectiveTransparency / 100d;
        var scale = isMouseDown ? 0.96d : IsMouseOver ? 1.025d : 1d;
        ShapeLogo.SetResourceReference(Shape.FillProperty, Checked ? "ColorBrush2" : "ColorBrush1");
        LabText.SetResourceReference(TextBlock.ForegroundProperty, Checked ? "ColorBrush2" : "ColorBrush1");
        LabText.FontWeight = Checked ? FontWeights.SemiBold : FontWeights.Normal;
        Effect = null; // Keep text and highlights on the normal retained rendering path.
        if (IsLoaded && allowAnimation && ModAnimation.AniControlEnabled == 0 && RenderTransform is ScaleTransform transform)
        {
            ModAnimation.AniStart(ModAnimation.AaColor(this, BackgroundProperty, target - Background, 150),
                "MyRadioButton Color " + Uuid);
            ModAnimation.AniStart(new[]
            {
                ModAnimation.AaOpacity(GlassSurface, opacity - GlassSurface.Opacity, 180),
                ModAnimation.AaScaleTransform(this, scale - transform.ScaleX, 180,
                    ease: new ModAnimation.AniEaseOutFluent())
            }, "MyRadioButton Glass " + Uuid);
        }
        else
        {
            ModAnimation.AniStop("MyRadioButton Color " + Uuid);
            Background = target;
            GlassSurface.Opacity = opacity;
            if (RenderTransform is ScaleTransform directTransform) directTransform.ScaleX = directTransform.ScaleY = scale;
        }
    }
}
