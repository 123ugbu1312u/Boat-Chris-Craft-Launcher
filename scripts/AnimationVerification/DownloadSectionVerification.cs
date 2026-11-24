using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using BCCL;

internal static class DownloadSectionVerification
{
    public static void Run()
    {
        var panel = new MyRevealStackPanel();
        var header = new Border { Height = 42 };
        var java = new MyListItem { Height = 40, Title = "Java 完整开发工具包", Type = MyListItem.CheckType.RadioBox };
        var nextHeader = new Border { Height = 42 };
        var nextItem = new MyListItem { Height = 36, Title = "Minecraft", Type = MyListItem.CheckType.RadioBox };
        panel.Children.Add(header);
        panel.Children.Add(java);
        panel.Children.Add(nextHeader);
        panel.Children.Add(nextItem);
        var title = (TextBlock)java.FindName("LabTitle");
        Layout();
        var titleMargin = title.Margin;
        var measuredRow = new MeasuredRow { Height = 40 };
        var measurePanel = new MyRevealStackPanel();
        measurePanel.Children.Add(measuredRow);
        MeasureLayout();
        var measureCount = measuredRow.MeasureCount;
        var sizeChanges = 0;
        java.SizeChanged += (_, _) => sizeChanges++;

        // The previous height animation switched the row's 40px layout branch
        // on its last frame. Sweep both directions, including repeated reversals.
        foreach (var progress in Enumerable.Range(0, 61).Select(i => i / 60d)
                     .Concat(Enumerable.Range(0, 61).Select(i => 1 - i / 60d))
                     .Concat(new[] { 0.1, 0.6, 0.3, 0.8, 0.05, 1.0 }))
        {
            MyRevealStackPanel.SetRevealProgress(java, progress);
            MyRevealStackPanel.SetRevealProgress(measuredRow, progress);
            MeasureLayout();
            Layout();
            Near(40, java.Height);
            Near(40, java.ActualHeight);
            Near(120 + 40 * progress, panel.DesiredSize.Height);
            Near(42 + 40 * progress, VisualTreeHelper.GetOffset(nextHeader).Y);
            if (title.Margin != titleMargin) throw new Exception("Java row text moved during reveal.");
            var clip = (RectangleGeometry)java.Clip;
            var origin = VisualTreeHelper.GetOffset(java).Y;
            Near(42, origin + clip.Rect.Top);
            Near(42 + 40 * progress, origin + clip.Rect.Bottom);
        }
        if (sizeChanges != 0) throw new Exception("Section reveal resized the inner Java row.");
        if (measuredRow.MeasureCount != measureCount) throw new Exception("Section reveal repeatedly measured row content.");

        // A fully collapsed/reopened item keeps its dimensions and participates
        // in the same radio group as the other download sections.
        java.Visibility = Visibility.Collapsed;
        Layout();
        Near(120, panel.DesiredSize.Height);
        java.Visibility = Visibility.Visible;
        MyRevealStackPanel.SetRevealProgress(java, 1);
        Layout();
        java.Checked = true;
        nextItem.Checked = true;
        if (java.Checked || !nextItem.Checked) throw new Exception("Download sections lost exclusive selection.");
        Console.WriteLine("PASS: Java section reveal/retract/reversal preserves the 40px row, text layout, clipped bounds and radio selection; no inner remeasure across 128 frames.");
        CheckLiveToggles();

        void Layout()
        {
            panel.Measure(new Size(220, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 220, panel.DesiredSize.Height));
            panel.UpdateLayout();
        }
        void MeasureLayout()
        {
            measurePanel.Measure(new Size(220, double.PositiveInfinity));
            measurePanel.Arrange(new Rect(0, 0, 220, measurePanel.DesiredSize.Height));
            measurePanel.UpdateLayout();
        }
    }

    private static void CheckLiveToggles()
    {
        var resources = System.Windows.Application.Current.Resources;
        var originalStyle = resources["ScrollViewerFullMargin"];
        var templateRoot = new FrameworkElementFactory(typeof(Grid));
        var presenter = new FrameworkElementFactory(typeof(ScrollContentPresenter), "PART_ScrollContentPresenter");
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        templateRoot.AppendChild(presenter);
        var scrollBar = new FrameworkElementFactory(typeof(MyScrollBar), "PART_VerticalScrollBar");
        scrollBar.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        templateRoot.AppendChild(scrollBar);
        var style = new Style(typeof(MyScrollViewer));
        style.Setters.Add(new Setter(Control.TemplateProperty,
            new ControlTemplate(typeof(MyScrollViewer)) { VisualTree = templateRoot }));
        resources["ScrollViewerFullMargin"] = style;
        var left = new PageDownloadLeft();
        var window = new Window { Content = left, Width = 260, Height = 760,
            Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        var originalSuppression = ModAnimation.AniControlEnabled;
        try
        {
            window.Show();
            Pump(40);
            // Live toggles must work even while another control temporarily
            // suppresses initialization effects.
            ModAnimation.AniControlEnabled = originalSuppression + 1;
            foreach (var (groupName, itemName) in new[] { ("GroupAutomatic", "ItemInstall"),
                         ("GroupCommunity", "ItemMod"), ("GroupJava", "ItemJava"), ("GroupManual", "ItemClient") })
            {
                var group = (Expander)left.FindName(groupName);
                var item = (MyListItem)left.FindName(itemName);
                group.IsExpanded = true;
                Pump(350);
                Near(1, MyRevealStackPanel.GetRevealProgress(item));

                group.IsExpanded = false;
                Pump(100);
                AssertTransition(item, "closing");
                var closingFrame = MyRevealStackPanel.GetRevealProgress(item);
                group.IsExpanded = true;
                Near(closingFrame, MyRevealStackPanel.GetRevealProgress(item));
                Pump(350);
                Near(1, MyRevealStackPanel.GetRevealProgress(item));
                if (item.Visibility != Visibility.Visible) throw new Exception("Stale closing callback hid a reopened section.");

                group.IsExpanded = false;
                Pump(350);
                Near(0, MyRevealStackPanel.GetRevealProgress(item));
                if (item.Visibility != Visibility.Collapsed) throw new Exception("Closing animation did not finish.");
                group.IsExpanded = true;
                Pump(100);
                AssertTransition(item, "opening");
                Pump(350);
                Near(1, MyRevealStackPanel.GetRevealProgress(item));
                Near(1, item.Opacity);

                var toggle = FindVisual<ToggleButton>(group)!;
                var arrow = (TextBlock)toggle.Template.FindName("Arrow", toggle);
                Near(90, ((RotateTransform)arrow.RenderTransform).Angle);
                group.IsExpanded = false;
                Pump(350);
                Near(0, ((RotateTransform)arrow.RenderTransform).Angle);
            }
        }
        finally
        {
            ModAnimation.AniControlEnabled = originalSuppression;
            window.Close();
            if (originalStyle is null) resources.Remove("ScrollViewerFullMargin");
            else resources["ScrollViewerFullMargin"] = originalStyle;
            ModAnimation.aniGroups.Clear();
        }
        Console.WriteLine("PASS: all four production sections visibly animate open AND closed; intermediate frames, reversal, final visibility and both arrow angles verified.");
    }

    private static void AssertTransition(MyListItem item, string direction)
    {
        var progress = MyRevealStackPanel.GetRevealProgress(item);
        if (progress <= 0 || progress >= 1 || item.Opacity <= 0 || item.Opacity >= 1 ||
            item.Visibility != Visibility.Visible)
            throw new Exception($"Section {direction} jumped instead of animating: progress={progress}, opacity={item.Opacity}.");
    }

    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.001)
            throw new Exception($"Download section: expected {expected}, got {actual}.");
    }

    private sealed class MeasuredRow : Border
    {
        public int MeasureCount { get; private set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            return base.MeasureOverride(availableSize);
        }
    }
}
