using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BCCL.Core.App;

namespace BCCL;

public partial class PageToolsDailyRecommendations
{
    private const double RowHeight = 64;
    private const double RowSpacing = 7;
    private const double RowStride = RowHeight + RowSpacing;

    private readonly DispatcherTimer _skeletonDelay = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private readonly DispatcherTimer _copyReset = new() { Interval = TimeSpan.FromSeconds(1.4) };
    private TextBlock? _copyFeedback;
    private int _rowsRevision;

    private static bool MotionEnabled => Config.Debug.AnimationSpeed < 30;

    private void InitializeMotion()
    {
        LoadingSkeleton.ItemsSource = Enumerable.Range(0, 5);
        _skeletonDelay.Tick += (_, _) =>
        {
            _skeletonDelay.Stop();
            LoadingSkeleton.Visibility = Visibility.Visible;
            if (MotionEnabled)
                LoadingSkeleton.BeginAnimation(OpacityProperty, new DoubleAnimation(0.60, 0.94, TimeSpan.FromMilliseconds(650))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
        };
        _copyReset.Tick += (_, _) => ResetCopyFeedback();
    }

    private void SwitchPane(bool showList)
    {
        var outgoing = showList ? (FrameworkElement)CategoryPanel : RankingPanel;
        var incoming = showList ? (FrameworkElement)RankingPanel : CategoryPanel;
        outgoing.Visibility = Visibility.Collapsed;
        outgoing.BeginAnimation(OpacityProperty, null);
        outgoing.RenderTransform = Transform.Identity;
        incoming.Visibility = Visibility.Visible;
        if (MotionEnabled)
            SlideIn(incoming, showList ? 16 : -16, 0, 0.72, 185, 0);
    }

    private void SetSkeleton(bool loading)
    {
        if (loading)
        {
            if (!_skeletonDelay.IsEnabled && LoadingSkeleton.Visibility != Visibility.Visible)
                _skeletonDelay.Start();
            return;
        }

        _skeletonDelay.Stop();
        LoadingSkeleton.Visibility = Visibility.Collapsed;
        LoadingSkeleton.BeginAnimation(OpacityProperty, null);
    }

    private void ClearRows()
    {
        _rowsRevision++;
        ResetCopyFeedback();
        Projects.Children.Clear();
    }

    private void PresentRows(IEnumerable<(string Key, object Item)> items, DataTemplate template, bool lazy, bool animate)
    {
        var rows = items.ToList();
        var previous = Projects.Children.OfType<FrameworkElement>().ToArray();
        var previousKeys = previous.Select(row => row.Tag as string).ToHashSet(StringComparer.Ordinal);
        var offset = RankingScroll.VerticalOffset;
        var first = (int)(offset / RowStride);
        if (offset > 0 && first < previous.Length)
        {
            var anchor = previous[first].Tag as string;
            var newIndex = rows.FindIndex(row => row.Key == anchor);
            if (newIndex >= 0)
                offset += (newIndex - first) * RowStride;
        }

        ClearRows();
        foreach (var (key, item) in rows)
        {
            Projects.Children.Add(lazy
                ? new MyVirtualizingElement<ContentControl>(() => CreateRow(key, item, template))
                {
                    Tag = key,
                    Height = RowHeight,
                    Margin = new Thickness(0, 0, 0, RowSpacing)
                }
                : CreateRow(key, item, template));
        }
        RankingScroll.ScrollToVerticalOffset(offset);
        SetSkeleton(false);

        if (!animate || !MotionEnabled)
            return;

        var revision = _rowsRevision;
        // Run after the new layout has applied the preserved scroll offset and created visible rows.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
        {
            if (revision != _rowsRevision || !RankingPanel.IsVisible)
                return;

            var top = RankingScroll.VerticalOffset;
            if (Math.Abs(top - Math.Clamp(offset, 0, RankingScroll.ScrollableHeight)) > 2)
                return;

            var bottom = top + RankingScroll.ViewportHeight;
            var delay = 0;
            for (var index = (int)(top / RowStride); index < Projects.Children.Count && index * RowStride < bottom; index++)
            {
                if (Projects.Children[index] is not ContentControl { Tag: string key } row || previousKeys.Contains(key))
                    continue;

                SlideIn(row, 0, 6, 0.52, 175, delay);
                delay = Math.Min(delay + 24, 120);
            }
        }));
    }

    private static ContentControl CreateRow(string key, object item, DataTemplate template) => new()
    {
        Tag = key,
        Content = item,
        ContentTemplate = template,
        Height = RowHeight,
        Margin = new Thickness(0, 0, 0, RowSpacing)
    };

    private static void SlideIn(FrameworkElement element, double x, double y, double opacity, int duration, int delay)
    {
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        element.BeginAnimation(OpacityProperty, EnterAnimation(opacity, 1, duration, delay));
        if (x != 0)
            transform.BeginAnimation(TranslateTransform.XProperty, EnterAnimation(x, 0, duration, delay));
        if (y != 0)
            transform.BeginAnimation(TranslateTransform.YProperty, EnterAnimation(y, 0, duration, delay));
    }

    private static DoubleAnimationUsingKeyFrames EnterAnimation(double from, double to, int duration, int delay)
    {
        var motion = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > 0)
            motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        motion.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + duration)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        return motion;
    }

    private void ShowCopyFeedback(TextBlock label)
    {
        if (!_serversOpen || !label.IsLoaded)
            return;

        ResetCopyFeedback();
        _copyFeedback = label;
        label.SetResourceReference(TextBlock.TextProperty, "Tools.Daily.Server.Copied");
        if (MotionEnabled)
            label.BeginAnimation(OpacityProperty, EnterAnimation(0.55, 1, 150, 0));
        _copyReset.Start();
    }

    private void ResetCopyFeedback()
    {
        _copyReset.Stop();
        if (_copyFeedback is { } label)
        {
            label.SetResourceReference(TextBlock.TextProperty, "Instance.Server.Card.CopyAddress");
            label.BeginAnimation(OpacityProperty, null);
            _copyFeedback = null;
        }
    }
}
