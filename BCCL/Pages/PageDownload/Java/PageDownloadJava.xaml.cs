using System.IO;
using System.Windows;
using Microsoft.Win32;
using BCCL.JavaDownloads;

namespace BCCL;

public partial class PageDownloadJava
{
    private CancellationTokenSource? operation;
    private bool initialized;

    public PageDownloadJava()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (!initialized) RefreshCatalog(); };
    }

    private void Refresh_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => RefreshCatalog();
    private void Cancel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => operation?.Cancel();

    public async void RefreshCatalog()
    {
        if (operation is not null) return;
        initialized = true;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        RefreshButton.IsEnabled = false;
        Status.Text = "正在刷新官方版本列表…";
        try
        {
            var versions = await JavaDownloadService.GetVersionsAsync(cancellation.Token);
            Versions.Children.Clear();
            foreach (var version in versions)
            {
                var item = new MyListItem { Title = $"Java {version}", Height = 42, Type = MyListItem.CheckType.Clickable, SvgIcon = "lucide/coffee" };
                item.Click += (_, _) => Install(version);
                Versions.Children.Add(item);
            }
            Status.Text = "";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { operation = null; RefreshButton.IsEnabled = true; }
    }

    private async void Install(int version)
    {
        if (operation is not null) return;
        var folder = new OpenFolderDialog { Title = $"选择 Java {version} 的安装位置（将在里面新建 Java{version}）", Multiselect = false };
        if (folder.ShowDialog() != true) return;
        var destination = Path.Combine(folder.FolderName, $"Java{version}");
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            ModMain.MyMsgBox($"{destination}\n\n此位置已存在，请选择其他文件夹。", "无法安装 Java");
            return;
        }
        var choice = ModMain.MyMsgBox($"Java {version} 完整 JDK\n安装位置：{destination}\n\n请选择发行方。Oracle 为默认选项，OpenJDK 使用 Eclipse Temurin。下载和使用须遵守所选发行方条款。", "确认 Java 下载", "Oracle（默认）", "OpenJDK", "取消");
        if (choice is not (1 or 2)) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(60));
        operation = cancellation;
        RefreshButton.IsEnabled = false;
        Versions.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        try
        {
            JavaPackage package;
            Status.Text = "正在获取官方安装包信息…";
            try { package = await JavaDownloadService.ResolveAsync(version, choice == 1 ? JavaVendor.Oracle : JavaVendor.OpenJdk, cancellation.Token); }
            catch (OracleDownloadUnavailableException ex)
            {
                var fallback = ModMain.MyMsgBox(ex.Message, "Oracle 下载", "改用 OpenJDK", "打开 Oracle 官网", "取消");
                if (fallback == 2) { ModBase.OpenWebsite(JavaDownloadService.OraclePage); Status.Text = "已打开 Oracle 官网，可登录下载，或重新选择 OpenJDK。"; return; }
                if (fallback != 1) { Status.Text = "下载已取消。"; return; }
                package = await JavaDownloadService.ResolveAsync(version, JavaVendor.OpenJdk, cancellation.Token);
            }
            await JavaDownloadService.InstallAsync(package, destination, new Progress<string>(message => Status.Text = message), cancellation.Token);
            try
            {
                await Task.Run(() =>
                {
                    if (ModJava.Javas.AddOrGet(Path.Combine(destination, "bin", "java.exe")) is null)
                        throw new InvalidOperationException("未能识别新安装的 Java。");
                    ModJava.Javas.SaveConfig();
                });
                Status.Text = $"Java {version} 已安装并添加到 Java 管理：{destination}";
            }
            catch (Exception ex) { Status.Text = $"Java 已安装到 {destination}，自动识别未完成，请在 Java 设置中手动添加。{ex.Message}"; }
        }
        catch (OperationCanceledException) { Status.Text = "下载已取消或超时，可以重新选择版本安装。"; }
        catch (Exception ex) { Status.Text = "安装失败：" + ex.Message; ModBase.Log(ex, "Java 下载失败"); }
        finally { operation = null; RefreshButton.IsEnabled = true; Versions.IsEnabled = true; CancelButton.Visibility = Visibility.Collapsed; }
    }
}
