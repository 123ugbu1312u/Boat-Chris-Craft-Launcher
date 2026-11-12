using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Documents;
using BCCL.Core.App.Localization;
using BCCL.Core.UI;

namespace BCCL;

public sealed class CpuOptimizationButton : MyButton
{
    private bool cpuOptimizationBusy;
    private Window? ownerWindow;
    private readonly Run cpuOptimizationStatus = new() { FontSize = 10 };

    public CpuOptimizationButton()
    {
        MinWidth = 145;
        SetResourceReference(ToolTipProperty, "Tools.Test.Cpu.ToolTip");
        SetResourceReference(AutomationProperties.NameProperty, "Tools.Test.Cpu.Title");
        Click += BtnCpuSingleCore_Click;
        LabText.TextAlignment = System.Windows.TextAlignment.Center;
        var title = new Run();
        title.SetResourceReference(Run.TextProperty, "Tools.Test.Cpu.Title");
        cpuOptimizationStatus.SetResourceReference(Run.TextProperty, "Tools.Test.Cpu.NotOptimized");
        Inlines.Add(title);
        Inlines.Add(new LineBreak());
        Inlines.Add(cpuOptimizationStatus);
        Loaded += (_, _) =>
        {
            CpuOptimizationTool.Changed += CpuOptimizationChanged;
            ownerWindow = Window.GetWindow(this);
            if (ownerWindow is not null) ownerWindow.Activated += CpuOptimizationActivated;
            CpuOptimizationChanged();
        };
        Unloaded += (_, _) =>
        {
            CpuOptimizationTool.Changed -= CpuOptimizationChanged;
            if (ownerWindow is not null) ownerWindow.Activated -= CpuOptimizationActivated;
            ownerWindow = null;
        };
    }

    private void CpuOptimizationActivated(object? sender, EventArgs e) => CpuOptimizationChanged();
    private void CpuOptimizationChanged() => ModBase.RunInUi(() =>
    {
        if (!cpuOptimizationBusy) cpuOptimizationStatus.SetResourceReference(Run.TextProperty, CpuOptimizationTool.StatusKey());
    });

    private async void BtnCpuSingleCore_Click(object sender, MouseButtonEventArgs e)
    {
        if (cpuOptimizationBusy) return;
        cpuOptimizationBusy = true;
        IsEnabled = false;
        cpuOptimizationStatus.SetResourceReference(Run.TextProperty, "Tools.Test.Cpu.Running");
        // Only processes launched/tracked by BCCL are eligible; never enumerate unrelated Java applications.
        var games = ModWatcher.mcWatcherList.Select(w => w.gameProcess)
            .Append(ModLaunch.mcLaunchProcess).Where(p => p is not null).Distinct().ToArray();
        try
        {
            var enabled = await Task.Run(() => CpuOptimizationTool.Toggle(games!));
            HintService.Hint(Lang.Text(enabled ? "Tools.Test.Cpu.Finished" : "Tools.Test.Cpu.Restored"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "CPU 单核优化或恢复失败");
            HintService.Hint(Lang.Text("Tools.Test.Cpu.Error"), HintType.Error);
        }
        finally
        {
            cpuOptimizationBusy = false;
            IsEnabled = true;
            CpuOptimizationChanged();
        }
    }
}
