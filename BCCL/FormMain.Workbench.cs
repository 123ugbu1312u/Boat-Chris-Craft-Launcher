using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using BCCL.Core.App;

namespace BCCL;

public partial class FormMain
{
    private void InitializeWorkbench()
    {
        foreach (var item in PanTitleSelect.Children.OfType<MyRadioButton>())
        {
            item.SetBinding(ToolTipProperty, new Binding(nameof(MyRadioButton.Text)) { Source = item });
            item.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(MyRadioButton.Text)) { Source = item });
        }
    }

    private void ApplyWorkbenchAppearance()
    {
        // Apply the chosen appearance once. Later personalization changes remain authoritative.
        if (!Config.Preference.WorkbenchThemeInitialized)
        {
            Config.Preference.Theme.ColorMode = ColorMode.Dark;
            Config.Preference.Theme.DarkColor = ColorTheme.SkyBlue;
            Config.Preference.WorkbenchThemeInitialized = true;
        }
    }

    private void ConfigureWorkspaceLayout(bool launch, bool fullWidth = false)
    {
        // Keep the original page hosts, lifecycle and launch progress; only place the
        // account/launch context on the right of the home workspace.
        WorkspaceContext.Visibility = fullWidth ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceContextColumn.Width = fullWidth ? new GridLength(0) : launch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        WorkspaceContentColumn.Width = launch ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(WorkspaceContext, launch ? 1 : 0);
        Grid.SetColumn(WorkspaceContent, launch ? 0 : 1);
        WorkspaceContext.Margin = fullWidth ? new Thickness(0) : launch ? new Thickness(0, 18, 20, 20) : new Thickness(16, 18, 0, 20);
    }
}
