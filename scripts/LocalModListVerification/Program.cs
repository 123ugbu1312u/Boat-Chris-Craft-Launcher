using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BCCL;
using BCCL.Core.App.Configuration;
using System.Diagnostics.CodeAnalysis;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;

    [STAThread]
    private static void Main(string[] args)
    {
        var config = typeof(BCCL.Core.App.Configuration.ConfigService);
        config.GetMethod("_InitializeConfigItems", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        config.GetField("_isConfigItemsInitialized", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        foreach (var key in ConfigService.KeySet)
        {
            ConfigService.TryGetConfigItemNoType(key, out var item);
            item!.GetType().GetField("<_Provider>k__BackingField", Hidden)!.SetValue(item, new MemoryProvider());
        }
        var app = new BCCL.Application();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.WriteLine(e.Exception);
            Environment.ExitCode = 1;
            e.Handled = true;
            app.Shutdown();
        };
        ModBase.pathTemp = System.IO.Path.Combine(AppContext.BaseDirectory, "verification-data") + "\\";
        System.IO.Directory.CreateDirectory(ModBase.pathTemp);
        ModAnimation.AniControlEnabled++;
        var page = new PageInstanceCompResource();
        var build = typeof(PageInstanceCompResource).GetMethod("BuildLocalCompItem", Hidden)!;
        var mark = typeof(ModLocalComp.LocalCompFile).GetMethod("MarkLoaded", Hidden)!;
        foreach (var count in new[] { 128, 1000 })
        {
            var entries = Enumerable.Range(0, count).Select(i =>
            {
                var entry = new ModLocalComp.LocalCompFile($@"C:\verification\mods\mod-{i:D4}.jar")
                { Name = $"Example Mod {i:D4}", Version = "1.0", Description = "Example description" };
                mark.Invoke(entry, null);
                return entry;
            }).ToList();
            var watch = Stopwatch.StartNew();
            var panel = new StackPanel();
            foreach (var entry in entries)
                panel.Children.Add((MyLocalCompItem)build.Invoke(page, [entry])!);
            var realize = typeof(MyLocalCompItem).GetMethod("EnsureVisuals", Hidden);
            foreach (var item in panel.Children.OfType<MyLocalCompItem>().Take(24))
                realize?.Invoke(item, null);
            Drain();
            panel.Measure(new Size(1000, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 1000, count * 44));
            Drain();
            Console.WriteLine($"Items={count}, construct/refresh/layout={watch.Elapsed.TotalMilliseconds:F1} ms, realized={panel.Children.OfType<MyLocalCompItem>().Count(i => i.Children.Count > 0)}");
        }
        if (args.Contains("--verify"))
        {
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { Verify(page, build, mark); }
                catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                finally { app.Shutdown(); }
            }));
            app.Run();
        }
        else app.Shutdown();
    }

    private static void Verify(PageInstanceCompResource page, MethodInfo build, MethodInfo mark)
    {
        var list = (StackPanel)page.FindName("PanList");
        var entries = Enumerable.Range(0, 1000).Select(i =>
        {
            var entry = new ModLocalComp.LocalCompFile($@"C:\verification\mods\mod-{i:D4}.jar" + (i % 2 == 1 ? ".disabled" : ""))
            { Name = $"Example Mod {i:D4}", Version = "1.0", Description = "A useful description" };
            mark.Invoke(entry, null);
            return entry;
        }).ToList();
        ModLocalComp.compResourceListLoader.output = entries;
        page.modItems.Clear();
        foreach (var entry in entries) page.modItems[entry.RawPath] = (MyLocalCompItem)build.Invoke(page, [entry])!;
        page.RefreshUI();
        Drain();
        Check(list.Children.Count == 1000, "all rows represented");
        Check(list.Children.OfType<MyLocalCompItem>().Count(i => i.Children.Count > 0) < 30, "only viewport rows realized");
        ((FrameworkElement)page.FindName("PanAllBack")).Visibility = Visibility.Visible;
        page.Measure(new Size(1000, 600));
        page.Arrange(new Rect(0, 0, 1000, 600));
        page.UpdateLayout();
        var scroll = (MyScrollViewer)page.FindName("PanBack");
        scroll.ScrollToBottom();
        page.UpdateLayout();
        Drain();
        Check(page.modItems[entries.Last().RawPath].Children.Count > 0, "scrolling to bottom realizes final rows");
        scroll.ScrollToHome();
        page.UpdateLayout();
        Drain();
        typeof(PageInstanceCompResource).GetMethod("ChangeAllSelected", Hidden)!.Invoke(page, [true]);
        Check(page.selectedMods.Count == 1000, "select all includes off-screen rows");
        var last = page.modItems[entries.Last().RawPath];
        typeof(MyLocalCompItem).GetMethod("EnsureVisuals", Hidden)!.Invoke(last, null);
        Drain();
        Check(last.Checked && last.Title == entries.Last().Name && last.Description.Contains("useful"), "late row preserves selection and content");
        page.Filter = PageInstanceCompResource.FilterType.Disabled;
        Check(list.Children.Count == 500 && list.Children.OfType<MyLocalCompItem>().All(i => i.Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Disabled), "disabled filter");
        page.Filter = PageInstanceCompResource.FilterType.Enabled;
        Check(list.Children.Count == 500 && list.Children.OfType<MyLocalCompItem>().All(i => i.Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine), "enabled filter");
        page.Filter = PageInstanceCompResource.FilterType.All;
        var search = typeof(PageInstanceCompResource).GetMethod("GetSearchResult", Hidden)!;
        var result = (List<ModLocalComp.LocalCompFile>)search.Invoke(page, ["Example Mod 0999"])!;
        Check(result.Contains(entries.Last()), "search includes off-screen rows");
        var before = list.Children.OfType<MyLocalCompItem>().ToArray();
        typeof(PageInstanceCompResource).GetMethod("DoSort", Hidden)!.Invoke(page, null);
        Check(before.SequenceEqual(list.Children.OfType<MyLocalCompItem>()), "sort preserves row identity and order");
        var coded = entries[900];
        coded.Name = "ignored";
        var codedRow = page.modItems[coded.RawPath];
        typeof(MyLocalCompItem).GetMethod("EnsureVisuals", Hidden)!.Invoke(codedRow, null);
        Drain();
        codedRow.Title = "§aGreen title";
        codedRow.Description = "§bBlue description";
        typeof(MyLocalCompItem).GetMethod("RefreshFormatting", Hidden)!.Invoke(codedRow, null);
        Drain();
        string InlineText(string name) => string.Concat(((TextBlock)codedRow.FindName(name)).Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
        Check(InlineText("LabTitle") == "Green title" && InlineText("LabInfo") == "Blue description", "Minecraft formatting codes preserved");
        var fixtureRoot = System.IO.Path.Combine(ModBase.pathTemp, Guid.NewGuid().ToString("N"));
        var modsRoot = System.IO.Path.Combine(fixtureRoot, "mods");
        System.IO.Directory.CreateDirectory(modsRoot);
        var fixturePath = System.IO.Path.Combine(modsRoot, "fixture.jar");
        System.IO.File.WriteAllText(fixturePath, "isolated fixture");
        var fixture = new ModLocalComp.LocalCompFile(fixturePath) { Name = "zzzz fixture", Version = "1.0" };
        mark.Invoke(fixture, null);
        entries.Add(fixture);
        page.modItems[fixture.RawPath] = (MyLocalCompItem)build.Invoke(page, [fixture])!;
        page.selectedMods.Add(fixture.RawPath);
        page.RefreshUI();
        Check(page.modItems[fixture.RawPath].Children.Count == 0, "toggle fixture starts off screen");
        PageInstanceLeft.McInstance = new McInstance(fixtureRoot) { IsLoaded = true };
        typeof(McInstance).GetField("<Info>k__BackingField", Hidden)!.SetValue(PageInstanceLeft.McInstance,
            new McInstanceInfo { VanillaName = "1.21.1", HasFabric = true });
        BCCL.Core.App.Config.Instance.IndieV2[PageInstanceLeft.McInstance.PathInstance] = true;
        var toggle = typeof(PageInstanceCompResource).GetMethod("EDModsCore", Hidden)!;
        Check((bool)toggle.Invoke(page, [new[] { fixture }, false])! && System.IO.File.Exists(fixturePath + ".disabled"), "disable off-screen fixture");
        var disabled = page.modItems[fixture.RawPath].Entry;
        Check(disabled.State == ModLocalComp.LocalCompFile.LocalFileStatus.Disabled && page.modItems[fixture.RawPath].Children.Count == 0, "disabled row stays deferred");
        Check((bool)toggle.Invoke(page, [new[] { disabled }, true])! && System.IO.File.Exists(fixturePath) && !System.IO.File.Exists(fixturePath + ".disabled"), "enable off-screen fixture");
        var enabledRow = page.modItems[fixture.RawPath];
        typeof(MyLocalCompItem).GetMethod("EnsureVisuals", Hidden)!.Invoke(enabledRow, null);
        Drain();
        Check(enabledRow.Checked && enabledRow.Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine && enabledRow.Title == "zzzz fixture", "toggle preserves selection and metadata");

        var owned = false;
        var finishes = 0;
        var shared = new ModLoader.LoaderTask<int, int>("Ownership verification", _ => { });
        new MyPageRight().PageLoaderInit(new MyLoading(), new Grid(), new Grid(), null, shared,
            _ => finishes++, autoRun: false, ownsLoader: () => owned);
        var finish = typeof(ModLoader.LoaderBase).GetMethod("RaisePreviewFinish", Hidden)!;
        finish.Invoke(shared, null);
        Check(finishes == 0, "unrelated page skips shared loader result");
        owned = true;
        finish.Invoke(shared, null);
        Check(finishes == 1, "owning page receives shared loader result");
        Drain();
        Console.WriteLine($"PASS: {checks} list regression checks.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private sealed class MemoryProvider : IConfigProvider
    {
        private readonly Dictionary<string, object?> values = new();
        public bool GetValue<T>(string key, [NotNullWhen(true)] out T? value, object? argument = null)
        {
            var found = values.TryGetValue(key, out var stored);
            value = found ? (T?)stored : default;
            return found;
        }
        public void SetValue<T>(string key, T value, object? argument = null) => values[key] = value;
        public void Delete(string key, object? argument = null) => values.Remove(key);
        public bool Exists(string key, object? argument = null) => values.ContainsKey(key);
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
