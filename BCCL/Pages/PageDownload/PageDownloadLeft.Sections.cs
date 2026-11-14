using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BCCL;

public partial class PageDownloadLeft
{
    private readonly Dictionary<MyListItem, double> sectionHeights = new();
    private readonly Dictionary<Expander, int> sectionRevisions = new();
    private Expander SectionFor(MyListItem item)
    {
        var id = (int)ModBase.Val(item.Tag);
        return id == 1 ? GroupAutomatic : id <= 8 ? GroupCommunity : id == 19 ? GroupJava : GroupManual;
    }

    private void InitializeSectionAnimations()
    {
        foreach (var item in PanItem.Children.OfType<MyListItem>())
            sectionHeights[item] = item.Height;
        foreach (var group in new[] { GroupAutomatic, GroupCommunity, GroupJava, GroupManual })
        {
            ResetSection(group);
            group.Expanded += (_, _) => AnimateSection(group);
            group.Collapsed += (_, _) => AnimateSection(group);
        }
        Unloaded += (_, _) =>
        {
            foreach (var group in new[] { GroupAutomatic, GroupCommunity, GroupJava, GroupManual })
                ResetSection(group);
        };
    }

    private void ResetSection(Expander group)
    {
        sectionRevisions[group] = sectionRevisions.GetValueOrDefault(group) + 1;
        foreach (var item in PanItem.Children.OfType<MyListItem>().Where(item => SectionFor(item) == group))
        {
            item.Visibility = group.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            item.BeginAnimation(HeightProperty, null);
            item.BeginAnimation(OpacityProperty, null);
            item.SetCurrentValue(OpacityProperty, 1d);
            if (item.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
            }
        }
    }

    private void AnimateSection(Expander group)
    {
        if (!IsLoaded || !IsVisible || ModAnimation.AniControlEnabled != 0)
        {
            ResetSection(group);
            return;
        }
        var expanded = group.IsExpanded;
        var revision = sectionRevisions[group] = sectionRevisions.GetValueOrDefault(group) + 1;
        foreach (var item in PanItem.Children.OfType<MyListItem>().Where(item => SectionFor(item) == group))
        {
            // Capture the currently displayed frame before replacing clocks, so reversing
            // an unfinished expansion/collapse continues smoothly from that exact state.
            var visible = item.Visibility == Visibility.Visible;
            var fromHeight = visible ? item.ActualHeight : 0;
            var fromOpacity = visible ? item.Opacity : 0;
            var fromOffset = visible && item.RenderTransform is TranslateTransform current ? current.Y : -8;
            var height = sectionHeights[item];
            if (!double.IsFinite(height)) height = Math.Max(1, item.DesiredSize.Height);
            var duration = expanded ? 220 : 180;
            DoubleAnimation Animation(double from, double to, int duration) => new(from, to, TimeSpan.FromMilliseconds(duration))
            {
                FillBehavior = FillBehavior.HoldEnd,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            item.Visibility = Visibility.Visible;
            var heightAnimation = Animation(fromHeight, expanded ? height : 0, duration);
            heightAnimation.Completed += (_, _) =>
            {
                // An old completion must never hide a section that was reopened.
                if (sectionRevisions[group] != revision) return;
                item.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                item.BeginAnimation(HeightProperty, null);
                item.BeginAnimation(OpacityProperty, null);
                item.SetCurrentValue(OpacityProperty, 1d);
                if (item.RenderTransform is TranslateTransform translate)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.Y = 0;
                }
            };
            item.BeginAnimation(HeightProperty, heightAnimation);
            item.BeginAnimation(OpacityProperty, Animation(fromOpacity, expanded ? 1 : 0, duration));
            var offset = new TranslateTransform();
            item.RenderTransform = offset;
            offset.BeginAnimation(TranslateTransform.YProperty, Animation(fromOffset, expanded ? 0 : -8, duration));
        }
    }
}
