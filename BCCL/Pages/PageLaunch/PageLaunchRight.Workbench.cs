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

    private void WorkspaceAction_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        var form = ModMain.frmMain;
        switch (element.Tag as string)
        {
            case "instances": form.PageChange(FormMain.PageType.InstanceSelect); break;
            case "install": form.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall); break;
            case "mods": form.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadMod); break;
            case "packs": form.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadPack); break;
            case "tasks": form.PageChange(FormMain.PageType.TaskManager); break;
            case "setup": form.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupLaunch); break;
        }
    }
}
