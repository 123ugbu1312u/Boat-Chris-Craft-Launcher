using System.Windows;
using System.Windows.Controls;
using BCCL.Multiplayer.Original;

namespace BCCL;

/// <summary>The complete original MCTier workspace, embedded without a second launcher sidebar.</summary>
public sealed class PageMultiplayerOriginal : MyPageRight
{
    private readonly Grid content = new();
    private readonly StackPanel status = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560, Margin = new Thickness(28) };
    private readonly TextBlock message = new() { Text = "正在加载联机…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button retry = new() { Content = "重新打开联机", Visibility = Visibility.Collapsed, Padding = new Thickness(18, 7, 18, 7), HorizontalAlignment = HorizontalAlignment.Center };
    private MCTierWindowHost? host;

    public PageMultiplayerOriginal()
    {
        Child = content;
        status.Children.Add(message);
        status.Children.Add(retry);
        content.Children.Add(status);
        retry.Click += (_, _) => Open();
        Loaded += (_, _) => { if (host == null) Open(); };
    }

    private void Open()
    {
        if (host != null) { content.Children.Remove(host); host.Dispose(); }
        message.Text = "正在加载联机…";
        status.Visibility = Visibility.Visible;
        retry.Visibility = Visibility.Collapsed;
        host = new MCTierWindowHost();
        host.StatusChanged += error =>
        {
            status.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            host.Visibility = error == null ? Visibility.Visible : Visibility.Hidden;
            message.Text = error ?? "";
            retry.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        };
        content.Children.Insert(0, host);
    }
}
