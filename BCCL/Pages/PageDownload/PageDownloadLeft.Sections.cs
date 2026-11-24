using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace BCCL;

public partial class PageDownloadLeft
{
    private readonly Dictionary<Expander, int> sectionRevisions = new();
    private Expander SectionFor(MyListItem item)
    {
        var id = (int)ModBase.Val(item.Tag);
        return id == 1 ? GroupAutomatic : id <= 8 ? GroupCommunity : id == 19 ? GroupJava : GroupManual;
    }

    private void InitializeSectionAnimations()
    {
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
            item.BeginAnimation(MyRevealStackPanel.RevealProgressProperty, null);
            MyRevealStackPanel.SetRevealProgress(item, group.IsExpanded ? 1 : 0);
            item.BeginAnimation(OpacityProperty, null);
            item.SetCurrentValue(OpacityProperty, 1d);
        }
    }

    private void AnimateSection(Expander group)
    {
        // Initialization/navigation may suppress other controls' effects. A live
        // section toggle still needs its own open/close transition.
        if (!IsLoaded || !IsVisible)
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
            var fromProgress = visible ? MyRevealStackPanel.GetRevealProgress(item) : 0;
            var fromOpacity = visible ? item.Opacity : 0;
            var target = expanded ? 1d : 0d;
            var duration = Math.Max(100, (int)Math.Round(280 * Math.Abs(target - fromProgress)));
            DoubleAnimation Animation(double from, double to, int duration) => new(from, to, TimeSpan.FromMilliseconds(duration))
            {
                FillBehavior = FillBehavior.HoldEnd,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            item.Visibility = Visibility.Visible;
            var revealAnimation = Animation(fromProgress, target, duration);
            revealAnimation.Completed += (_, _) =>
            {
                // An old completion must never hide a section that was reopened.
                if (sectionRevisions[group] != revision) return;
                item.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                MyRevealStackPanel.SetRevealProgress(item, target);
                item.BeginAnimation(MyRevealStackPanel.RevealProgressProperty, null);
                item.BeginAnimation(OpacityProperty, null);
                item.SetCurrentValue(OpacityProperty, 1d);
            };
            item.BeginAnimation(MyRevealStackPanel.RevealProgressProperty, revealAnimation);
            item.BeginAnimation(OpacityProperty, Animation(fromOpacity, expanded ? 1 : 0, duration));
        }
    }
}
