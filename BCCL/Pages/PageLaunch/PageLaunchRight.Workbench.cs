using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BCCL;

public partial class PageLaunchRight
{
    private void WorkspaceHero_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // ClipToBounds only clips to a rectangle; the oversized rings must also
        // respect the card's rounded corners, including after a window resize.
        var content = (FrameworkElement)sender;
        var radius = WorkspaceHeroCard.CornerRadius.TopRight;
        var clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
        clip.Freeze();
        content.Clip = clip;
    }

    private void InitializeWorkspaceSummary()
    {
        // Observe the existing launch status, including loading/errors/instance changes.
        // This adds no scanner, timer or duplicate instance cache.
        LabWorkspaceVersion.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
            new Binding("Text") { Source = ModMain.frmLaunchLeft.LabVersion });
    }
}
