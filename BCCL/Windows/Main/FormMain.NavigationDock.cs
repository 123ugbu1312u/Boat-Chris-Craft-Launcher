using System.Windows;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Diagnostics;

namespace BCCL;

public partial class FormMain
{
    private const double NavigationEdgeWidth = 76;
    private bool navigationExpanded;
    private bool navigationRendering;
    private long navigationStartTick;
    private double navigationFromWidth, navigationTargetWidth, navigationDurationMs;
    private MyPageLeft? multiplayerLeft;
    private PageMultiplayerOriginal? multiplayerPage;

    public void ShowMultiplayerExtras(bool extras)
    {
        if (pageCurrent.page != PageType.Multiplayer || multiplayerLeft is null) return;
        MyPageRight target = InitializeMultiplayerWorkspace();
        if (ReferenceEquals(PanMainRight.Child, target)) return;
        PageChangeAnim(multiplayerLeft, target);
    }

    private void InitializeNavigationDock()
    {
        foreach (var item in PanTitleSelect.Children.OfType<MyRadioButton>())
            item.LabText.Opacity = 0d;
        Loaded += (_, _) => UpdateNavigationDockSize();
        BCCL.Core.App.Localization.LocalizationService.LanguageChanged += NavigationLanguageChanged;
        Closed += (_, _) => BCCL.Core.App.Localization.LocalizationService.LanguageChanged -= NavigationLanguageChanged;
        Closed += (_, _) => StopNavigationRendering();
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(NavigationDock_PreviewMouseDown), true);
        Deactivated += (_, _) => SetNavigationExpanded(false);
    }

    private void NavigationLanguageChanged() => Dispatcher.BeginInvoke(new Action(UpdateNavigationDockSize));

    private double GetNavigationExpandedWidth()
    {
        var textWidth = PanTitleSelect.Children.OfType<MyRadioButton>().Max(item =>
        {
            var icon = item.LogoHost;
            var label = item.LabText;
            return icon.Width + icon.Margin.Left + icon.Margin.Right +
                   MeasureNavigationText(label, FontWeights.SemiBold) + label.Margin.Left + label.Margin.Right;
        });
        var brandWidth = MeasureNavigationText(LabNavigationBrand, LabNavigationBrand.FontWeight) +
                         LabNavigationBrand.Margin.Left + LabNavigationBrand.Margin.Right;
        return Math.Ceiling(Math.Max(NavigationEdgeWidth,
            Math.Max(brandWidth, textWidth + PanTitleSelect.Margin.Left + PanTitleSelect.Margin.Right)));
    }

    private static double MeasureNavigationText(TextBlock label, FontWeight weight) =>
        new FormattedText(label.Text ?? string.Empty, CultureInfo.CurrentUICulture, label.FlowDirection,
            new Typeface(label.FontFamily, label.FontStyle, weight, label.FontStretch), label.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private void NavigationDock_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!navigationExpanded || !BCCL.Core.App.Config.Preference.NavigationClickAway ||
            e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle) ||
            e.OriginalSource is not DependencyObject source ||
            ReferenceEquals(source, NavigationDock) || NavigationDock.IsAncestorOf(source))
            return;

        SetNavigationExpanded(false);
        // Do not mark the event handled: the clicked page control still receives it.
    }

    private void UpdateNavigationDockSize()
    {
        if (NavigationSlide is null || NavigationDock is null || PanTitle is null ||
            PanTitleSelect is null || LabNavigationBrand is null || WorkspaceViewport is null) return;
        PanTitle.Width = GetNavigationExpandedWidth();
        StopNavigationRendering();
        NavigationDock.Width = navigationExpanded ? PanTitle.Width : NavigationEdgeWidth;
        NavigationSlide.BeginAnimation(TranslateTransform.XProperty, null);
        NavigationSlide.X = 0;
        NavigationDock.BeginAnimation(WidthProperty, null);
    }

    private void NavigationDock_MouseEnter(object sender, MouseEventArgs e)
    {
        SetNavigationExpanded(true);
    }
    private void NavigationDock_MouseLeave(object sender, MouseEventArgs e) => SetNavigationExpanded(false);
    private void NavigationDock_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SetNavigationExpanded(true);
    }
    private void NavigationDock_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!NavigationDock.IsMouseOver && !NavigationDock.IsKeyboardFocusWithin)
            SetNavigationExpanded(false);
    }

    private void SetNavigationExpanded(bool expanded)
    {
        if (navigationExpanded == expanded) return;
        navigationExpanded = expanded;
        PanTitle.Width = GetNavigationExpandedWidth();
        var target = expanded ? PanTitle.Width : NavigationEdgeWidth;
        var current = NavigationDock.Width;
        var edgeOpacity = expanded ? 0d : 1d;
        StopNavigationRendering();
        NavigationDock.BeginAnimation(WidthProperty, null);
        // The icon rail stays in place while the text drawer opens. A single
        // acceleration/deceleration curve avoids the old overshoot and keeps
        // reversal continuous, even when Windows client-area animation is off.
        var distance = Math.Abs(target - current);
        navigationFromWidth = current;
        navigationTargetWidth = target;
        navigationDurationMs = Math.Max(100, (expanded ? 280 : 220) * distance /
            Math.Max(1, PanTitle.Width - NavigationEdgeWidth));
        navigationStartTick = Stopwatch.GetTimestamp();
        navigationRendering = true;
        CompositionTarget.Rendering += NavigationRendering;
        foreach (var item in PanTitleSelect.Children.OfType<MyRadioButton>())
        {
            // Keep text hidden behind the collapsed rail despite the tighter gap.
            ToolTipService.SetIsEnabled(item, !expanded);
            var label = item.LabText;
            var previous = label.Opacity;
            label.BeginAnimation(OpacityProperty, null);
            label.Opacity = expanded ? 1d : 0d;
            label.BeginAnimation(OpacityProperty, new DoubleAnimation(previous, label.Opacity,
                TimeSpan.FromMilliseconds(expanded ? 140 : 80)) { FillBehavior = FillBehavior.Stop });
        }
        var previousOpacity = NavigationEdge.Opacity;
        NavigationEdge.BeginAnimation(OpacityProperty, null);
        NavigationEdge.Opacity = edgeOpacity;
        NavigationEdge.BeginAnimation(OpacityProperty, new DoubleAnimation(previousOpacity, edgeOpacity,
            TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
    }

    private void StopNavigationRendering()
    {
        if (!navigationRendering) return;
        CompositionTarget.Rendering -= NavigationRendering;
        navigationRendering = false;
    }

    private void NavigationRendering(object? sender, EventArgs e)
    {
        var progress = Stopwatch.GetElapsedTime(navigationStartTick).TotalMilliseconds / navigationDurationMs;
        // Update the real layout on every presented frame, including the page width.
        // Never freeze the page or defer its final reflow to a completion callback.
        NavigationDock.Width = GetNavigationFrameWidth(navigationFromWidth, navigationTargetWidth, progress);
        if (progress >= 1) StopNavigationRendering();
    }

    internal static double GetNavigationFrameWidth(double from, double to, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        return from + (to - from) * eased;
    }
}
