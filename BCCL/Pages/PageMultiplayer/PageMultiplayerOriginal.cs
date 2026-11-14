using System.Windows;
using System.Windows.Controls;
using BCCL.Multiplayer.Original;

namespace BCCL;

/// <summary>Page lifecycle and inline errors; the native surface stays in the main window.</summary>
public sealed class PageMultiplayerOriginal : MyPageRight
{
    private readonly Border surface;
    private readonly StackPanel status = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560, Margin = new Thickness(28) };
    private readonly TextBlock message = new() { Text = "正在加载联机…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button retry = new() { Content = "重新打开联机", Visibility = Visibility.Collapsed, Padding = new Thickness(18, 7, 18, 7), HorizontalAlignment = HorizontalAlignment.Center };
    private MCTierWindowHost? host;
    private Task? preparation;
    private bool active;
    private string? lastHostError;

    public PageMultiplayerOriginal(Border surface)
    {
        this.surface = surface;
        Child = status;
        status.Children.Add(message);
        status.Children.Add(retry);
        retry.Click += async (_, _) => await PrepareAsync(force: true);
        PageExit += () => SetWorkspaceActive(false);
    }

    internal void SetWorkspaceActive(bool value)
    {
        active = value;
        surface.IsHitTestVisible = value;
        host?.SetWorkspaceActive(value && lastHostError == null);
    }

    internal Task PrepareAsync(bool force = false)
    {
        if (!force && preparation != null) return preparation;
        if (preparation is { IsCompleted: false }) return preparation;
        return preparation = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (host != null) { surface.Child = null; host.Dispose(); }
        message.Text = "正在准备联机…";
        lastHostError = null;
        status.Visibility = Visibility.Visible;
        retry.Visibility = Visibility.Collapsed;
        var nextHost = new MCTierWindowHost();
        host = nextHost;
        nextHost.StatusChanged += error =>
        {
            if (host != nextHost) return;
            if (error != null && error != lastHostError)
                ModBase.Log("[Multiplayer] " + error, ModBase.LogLevel.Debug);
            lastHostError = error;
            status.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            nextHost.SetWorkspaceActive(active && error == null);
            message.Text = error ?? "";
            retry.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        };
        // A permanent, measured host is connected before the process starts.
        surface.Child = nextHost;
        try
        {
            ModBase.Log("[Multiplayer] 开始后台预加载并绘制联机界面", ModBase.LogLevel.Debug);
            await MCTierEmbeddedSession.Current.EnsureReadyAsync(new Progress<string>(text => message.Text = text));
            await nextHost.PrepareFrameAsync();
            ModBase.Log("[Multiplayer] 后台联机界面首帧已绘制", ModBase.LogLevel.Debug);
        }
        catch (Exception error)
        {
            ModBase.Log(error, "[Multiplayer] 联机组件准备失败", ModBase.LogLevel.Debug);
            lastHostError = error.Message;
            message.Text = "联机组件准备失败：" + error.Message;
            retry.Visibility = Visibility.Visible;
            status.Visibility = Visibility.Visible;
        }
    }
}
