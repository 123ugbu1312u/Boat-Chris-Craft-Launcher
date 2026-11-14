using System.IO;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using BCCL;
using BCCL.Core.App;
using BCCL.Core.App.Configuration;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Paths.Data = Path.Combine(AppContext.BaseDirectory, "verification-data");
        Paths.SharedLocalData = Paths.Data;
        Directory.CreateDirectory(Paths.Data);
        // Same harmless configuration initialization as the existing UI verification harness.
        var config = typeof(ConfigService);
        config.GetMethod("_InitializeConfigItems", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        config.GetField("_isConfigItemsInitialized", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        foreach (var key in ConfigService.KeySet)
        {
            ConfigService.TryGetConfigItemNoType(key, out var item);
            item!.GetType().GetField("<_Provider>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(item, new MemoryProvider());
        }
        var app = new BCCL.Application();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(Paths.Data, "ui-error.log"), e.Exception.ToString());
            e.Handled = true; app.Shutdown(1);
        };
        ModAnimation.AniControlEnabled++;
        ThemeManager.ThemeRefreshMain();
        var root = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);
        var layers = new Grid();
        root.Children.Add(layers);
        var workspace = new ContentControl();
        var surface = new Border();
        layers.Children.Add(workspace);
        layers.Children.Add(surface);
        var page = new PageMultiplayerOriginal(surface);
        workspace.Content = page;
        var away = new Button { Content = "切到其他页面", Padding = new Thickness(12, 6, 12, 6) };
        var back = new Button { Content = "返回联机", Padding = new Thickness(12, 6, 12, 6) };
        var activate = typeof(PageMultiplayerOriginal).GetMethod("SetWorkspaceActive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        away.Click += (_, _) => { activate.Invoke(page, [false]); workspace.Content = new TextBlock { Text = "房间应在后台继续运行。", Margin = new Thickness(30) }; };
        back.Click += (_, _) => { workspace.Content = page; activate.Invoke(page, [true]); };
        tools.Children.Add(away); tools.Children.Add(back);
        var window = new Window { Title = "BCCL 原版 MCTier 内嵌验证", Width = 1050, Height = 730, MinWidth = 700, MinHeight = 520, Content = root };
        window.Loaded += async (_, _) =>
        {
            var prepare = (Task)typeof(PageMultiplayerOriginal).GetMethod("PrepareAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [false])!;
            await prepare;
            activate.Invoke(page, [true]);
        };
        window.Closed += (_, _) => app.Shutdown();
        app.Run(window);
    }

    private sealed class MemoryProvider : IConfigProvider
    {
        private readonly Dictionary<string, object?> values = [];
        public bool GetValue<T>(string key, [NotNullWhen(true)] out T? value, object? argument = null)
        { var found = values.TryGetValue(key, out var stored); value = found ? (T?)stored : default; return found; }
        public void SetValue<T>(string key, T value, object? argument = null) => values[key] = value;
        public void Delete(string key, object? argument = null) => values.Remove(key);
        public bool Exists(string key, object? argument = null) => values.ContainsKey(key);
    }
}
