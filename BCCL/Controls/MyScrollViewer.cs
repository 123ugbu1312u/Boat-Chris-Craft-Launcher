using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BCCL.Core.UI.Controls;

namespace BCCL;

public class MyScrollViewer : ScrollViewer
{
    private readonly string scrollAnimationName = "MyScrollViewer Offset " + ModBase.GetUuid();
    private double realOffset;
    private double scrollTarget;
    private double requestedOffset;

    public MyScrollBar? scrollBar;

    public MyScrollViewer()
    {
        PreviewMouseWheel += MyScrollViewer_PreviewMouseWheel;
        ScrollChanged += MyScrollViewer_ScrollChanged;
        IsVisibleChanged += MyScrollViewer_IsVisibleChanged;
        PreviewGotKeyboardFocus += MyScrollViewer_PreviewGotKeyboardFocus;
    }

    public double DeltaMult { get; set; } = 1d;
    public bool SmoothScrolling { get; set; }

    private void MyScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || ScrollableHeight <= 0d)
            return;

        var src = e.Source;
        if (Content is FrameworkElement element && element.TemplatedParent is null)
        {
            switch (src)
            {
                case ComboBox { IsDropDownOpen: true }:
                case TextBox { AcceptsReturn: true }:
                case ComboBoxItem:
                case CheckBox:
                    return;
            }
        }

        e.Handled = true;
        if (SmoothScrolling)
            SmoothScrollBy(-e.Delta * DeltaMult);
        else
            PerformVerticalOffsetDelta(-e.Delta);

        Tooltip.Dismiss();
    }

    public void PerformVerticalOffsetDelta(double delta)
    {
        if (SmoothScrolling)
        {
            SmoothScrollTo(VerticalOffset + delta * DeltaMult);
            return;
        }

        ModAnimation.AniStart(ModAnimation.AaDouble(animDelta =>
        {
            realOffset = ModBase.MathClamp(realOffset + (double)animDelta, 0d, ExtentHeight - ActualHeight);
            ScrollToVerticalOffset(realOffset);
        }, delta * DeltaMult, 300, 0, new ModAnimation.AniEaseOutFluent((ModAnimation.AniEasePower)6), false));
    }

    private void SmoothScrollBy(double delta)
    {
        if (scrollBar?.Track?.Thumb?.IsDragging == true)
            return;

        var isAnimating = ModAnimation.AniIsRun(scrollAnimationName);
        var origin = isAnimating ? requestedOffset : VerticalOffset;
        // 连续同向滚动累加目标，反向时从当前位置开始。
        if (isAnimating && (scrollTarget - origin) * delta > 0)
            origin = scrollTarget;

        SmoothScrollTo(origin + delta);
    }

    private void SmoothScrollTo(double offset)
    {
        var target = Math.Clamp(offset, 0d, ScrollableHeight);
        var canAnimate = IsVisible && ModAnimation.AniControlEnabled == 0;
        var isAnimating = ModAnimation.AniIsRun(scrollAnimationName);
        if (isAnimating && target == scrollTarget && canAnimate)
            return;

        ModAnimation.AniStop(scrollAnimationName);
        scrollTarget = target;
        // ScrollToVerticalOffset 可能还在等待布局，继续使用上一帧已请求的位置。
        if (!isAnimating)
            requestedOffset = VerticalOffset;
        if (!canAnimate || Math.Abs(target - requestedOffset) < 0.01d)
        {
            ScrollToVerticalOffset(target);
            return;
        }

        ModAnimation.AniStart(ModAnimation.AaDouble(delta =>
        {
            requestedOffset = Math.Clamp(requestedOffset + (double)delta, 0d, ScrollableHeight);
            ScrollToVerticalOffset(requestedOffset);
        }, target - requestedOffset, 220, ease: new ModAnimation.AniEaseOutFluent()), scrollAnimationName);
    }

    private void MyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (SmoothScrolling && e.OriginalSource == this && ModAnimation.AniIsRun(scrollAnimationName) &&
            (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 ||
             (e.VerticalChange != 0 && Math.Abs(e.VerticalOffset - requestedOffset) > 0.01d)))
            ModAnimation.AniStop(scrollAnimationName);

        realOffset = VerticalOffset;
        if (ModMain.frmMain is not null &&
            (e.VerticalChange != 0 || e.ViewportHeightChange != 0))
            ModMain.frmMain.BtnExtraBack.ShowRefresh();
    }

    private void MyScrollViewer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (SmoothScrolling && !IsVisible)
            ModAnimation.AniStop(scrollAnimationName);

        ModMain.frmMain?.BtnExtraBack?.ShowRefresh();
    }

    public override void OnApplyTemplate()
    {
        if (scrollBar is not null)
        {
            CommandManager.RemovePreviewExecutedHandler(scrollBar, ScrollBar_PreviewExecuted);
            scrollBar.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(ScrollBar_DragStarted));
        }

        base.OnApplyTemplate();
        scrollBar = GetTemplateChild("PART_VerticalScrollBar") as MyScrollBar;
        if (SmoothScrolling && scrollBar is not null)
        {
            CommandManager.AddPreviewExecutedHandler(scrollBar, ScrollBar_PreviewExecuted);
            scrollBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(ScrollBar_DragStarted), true);
        }
    }

    private void ScrollBar_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ScrollBar.PageUpCommand && e.Command != ScrollBar.PageDownCommand)
            return;

        e.Handled = true;
        SmoothScrollBy(e.Command == ScrollBar.PageUpCommand ? -ViewportHeight : ViewportHeight);
    }

    private void ScrollBar_DragStarted(object sender, DragStartedEventArgs e) =>
        ModAnimation.AniStop(scrollAnimationName);

    private void MyScrollViewer_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is MySlider)
            e.Handled = true; // #3854，阻止获得焦点时自动滚动
    }
}
