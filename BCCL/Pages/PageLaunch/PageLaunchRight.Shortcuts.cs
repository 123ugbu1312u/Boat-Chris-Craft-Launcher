using System.Windows.Input;
using BCCL.Core.App;
using PageType = BCCL.FormMain.PageType;
using PageSubType = BCCL.FormMain.PageSubType;

namespace BCCL;

public partial class PageLaunchRight
{
    private sealed record WorkspaceShortcut(string TextKey, PageType Page, PageSubType SubPage = PageSubType.Default,
        string? ToolTipKey = null);

    private static readonly WorkspaceShortcut[] _OriginalWorkspaceShortcuts =
    [
        new("Workbench.Library", PageType.InstanceSelect, ToolTipKey: "Workbench.LibraryHint"),
        new("Workbench.Install", PageType.Download, PageSubType.DownloadInstall, "Workbench.InstallHint"),
        new("Workbench.Mods", PageType.Download, PageSubType.DownloadMod),
        new("Workbench.Packs", PageType.Download, PageSubType.DownloadPack),
        new("Workbench.Tasks", PageType.TaskManager),
        new("Workbench.Setup", PageType.Setup, PageSubType.SetupLaunch),
    ];

    private static readonly WorkspaceShortcut[] _WorkspaceShortcuts =
    [
        .._OriginalWorkspaceShortcuts,
        new("Download.Left.DataPack", PageType.Download, PageSubType.DownloadDataPack),
        new("Download.Left.ResourcePack", PageType.Download, PageSubType.DownloadResourcePack),
        new("Download.Left.Shader", PageType.Download, PageSubType.DownloadShader),
        new("Download.Left.World", PageType.Download, PageSubType.DownloadWorld),
        new("Setup.Left.Item.Java", PageType.Setup, PageSubType.SetupJava),
        new("Setup.Left.Item.GameManage", PageType.Setup, PageSubType.SetupGameManage),
        new("Setup.Left.Item.Ui", PageType.Setup, PageSubType.SetupUI),
        new("Setup.Language.Title", PageType.Setup, PageSubType.SetupLauncherLanguage),
        new("Setup.Left.Item.Misc", PageType.Setup, PageSubType.SetupLauncherMisc),
        new("Setup.Left.Item.Update", PageType.Setup, PageSubType.SetupUpdate),
        new("Setup.Left.Item.Log", PageType.Setup, PageSubType.SetupLog),
        new("Tools.Left.Utilities.Title", PageType.Tools, PageSubType.ToolsTest),
        new("Tools.Left.Multiplayer", PageType.Multiplayer),
    ];

    private void InitializeWorkspaceShortcuts()
    {
        var buttons = WorkspaceShortcutGrid.Children.OfType<MyButton>().ToArray();
        var savedKeys = States.UI.WorkbenchLastShortcuts;
        var previousKeys = string.IsNullOrEmpty(savedKeys)
            ? _OriginalWorkspaceShortcuts.Select(shortcut => shortcut.TextKey).ToHashSet(StringComparer.Ordinal)
            : savedKeys.Split(',', StringSplitOptions.RemoveEmptyEntries).Take(buttons.Length).ToHashSet(StringComparer.Ordinal);
        var available = _WorkspaceShortcuts.Where(shortcut => !previousKeys.Contains(shortcut.TextKey)).ToArray();
        Random.Shared.Shuffle(available.AsSpan());

        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            var shortcut = available[index];
            button.Tag = shortcut;
            button.SetResourceReference(MyButton.TextProperty, shortcut.TextKey);
            if (shortcut.ToolTipKey is { } toolTipKey)
                button.SetResourceReference(ToolTipProperty, toolTipKey);
        }

        States.UI.WorkbenchLastShortcuts = string.Join(',', available.Take(buttons.Length).Select(shortcut => shortcut.TextKey));
    }

    private void WorkspaceShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is MyButton { Tag: WorkspaceShortcut shortcut })
            ModMain.frmMain.PageChange(shortcut.Page, shortcut.SubPage);
    }
}
