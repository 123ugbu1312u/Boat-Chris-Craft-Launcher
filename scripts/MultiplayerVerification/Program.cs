using System.Diagnostics;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BCCL;
using BCCL.Core.App;
using BCCL.Core.App.Configuration;
using BCCL.Core.Link.MCTier;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "verification-data");
        Paths.Data = data;
        Paths.SharedLocalData = data;
        Directory.CreateDirectory(data);
        if (args.Length == 4 && args[0] == "--mctier-helper")
        {
            try
            {
                var method = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierHelper")!.GetMethod("RunAsync")!;
                ((Task)method.Invoke(null, [args[1], int.Parse(args[2]), long.Parse(args[3])])!).GetAwaiter().GetResult();
            }
            catch { Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--fast-room-live")) { FastRoomAsync().GetAwaiter().GetResult(); return; }
        if (args.Contains("--verify-admin-guard")) { VerifyAdminGuardAsync(args.Length > 1 ? args.Last() : null).GetAwaiter().GetResult(); return; }
        if (args.Contains("--network")) { NetworkAsync(args.Last(), args.Contains("--screen-60")).GetAwaiter().GetResult(); return; }
        if (args.Contains("--package-helper")) { PackageHelperAsync(args.Last()).GetAwaiter().GetResult(); return; }
        if (args.Contains("--session")) { SessionAsync().GetAwaiter().GetResult(); return; }
        if (args.Contains("--directory-live")) { DirectoryAsync().GetAwaiter().GetResult(); return; }
        if (args.Contains("--managed-session-live")) { ManagedSessionAsync().GetAwaiter().GetResult(); return; }
        InitializeConfig();
        var app = new BCCL.Application();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) => { File.WriteAllText(Path.Combine(data, "ui-error.log"), e.Exception.ToString()); e.Handled = true; app.Shutdown(1); };
        ModAnimation.AniControlEnabled++;
        ThemeManager.ThemeRefreshMain();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new PageMultiplayerLeft();
        var page = new PageMultiplayer();
        Grid.SetColumn(page, 1);
        grid.Children.Add(left);
        grid.Children.Add(page);
        if (args.Contains("--verify-create-live") || args.Contains("--verify-create-live-once"))
        {
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await VerifyCreationAsync(page, args.Contains("--verify-create-live-once")); }
                catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                finally { app.Shutdown(); }
            }));
            app.Run(); return;
        }
        if (args.Contains("--extras-ui") || args.Contains("--verify-extras-ui"))
        {
            grid.Children.Remove(page);
            var extras = new PageMultiplayerExtras(); Grid.SetColumn(extras, 1); grid.Children.Add(extras);
            if (args.Contains("--verify-extras-ui"))
            {
                app.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await VerifyExtrasUiAsync(extras, grid); }
                    catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                    finally { app.Shutdown(); }
                }));
                app.Run(); return;
            }
        }
        if (args.Contains("--verify-name-live"))
        {
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await VerifyNameEntryAsync(page); }
                catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                finally { app.Shutdown(); }
            }));
            app.Run();
            return;
        }
        if (args.Contains("--verify-ui"))
        {
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { VerifyUi(page, left, grid); }
                catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                finally { app.Shutdown(); }
            }));
            app.Run();
            return;
        }
        var window = new Window { Title = "BCCL 联机页面验证", Width = 1020, Height = 620, MinWidth = 700, MinHeight = 450, Content = grid };
        window.SetResourceReference(Window.BackgroundProperty, "ColorBrushBackground");
        window.Closed += (_, _) => app.Shutdown();
        app.Run(window);
    }

    private static async Task VerifyCreationAsync(PageMultiplayer page, bool once)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierSession")!;
        var session = type.GetProperty("Current")!.GetValue(null)!;
        object? Call(string method, params object[] values) => typeof(PageMultiplayer).GetMethod(method, hidden)!.Invoke(page, values);
        var mode = typeof(PageMultiplayer).GetNestedType("EntryMode", BindingFlags.NonPublic)!;
        typeof(PageMultiplayer).GetField("pageActive", hidden)!.SetValue(page, true);
        try
        {
            foreach (var password in once ? new[] { "1" } : new[] { "123456", "" })
            {
                Call("ShowEntry", Enum.Parse(mode, "Create"));
                ((MyTextBox)page.FindName("TextRoom")).Text = "BCCL-create-QA-" + Guid.NewGuid().ToString("N");
                ((PasswordBox)page.FindName("TextPassword")).Password = password;
                ((MyTextBox)page.FindName("TextNickname")).Text = "BCCL-QA-Host";
                Call("Start", true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                while ((bool)typeof(PageMultiplayer).GetField("busy", hidden)!.GetValue(page)!) await Task.Delay(100, timeout.Token);
                if (!(bool)type.GetProperty("Running")!.GetValue(session)!) throw new Exception(((TextBlock)page.FindName("LabNotice")).Text);
                var request = (MCTierStartRequest)type.GetProperty("Request")!.GetValue(session)!;
                if (request.Room.Password != password || !request.ManagedRoom || !request.RequireAdministrator) throw new Exception("Creation used incorrect parameters");
                using var guest = await MCTierDirectoryLease.JoinAllocatedAsync(request.Room, "BCCL-QA-Guest", MCTierDirectoryLease.ValidateEndpoint(MCTierDirectoryLease.DefaultEndpoint), timeout.Token);
                Console.WriteLine("PASS: actual page Create action completes user-approved elevation, verified IPC, real core/IP/registration and admits a named guest with " + (password.Length == 0 ? "a blank password" : "a numeric password"));
                if (type.GetProperty("Extras")!.GetValue(session) is not MCTierExtrasHub) throw new Exception("Other services did not start");
                Console.WriteLine("PASS: other services start alongside the real room without requiring its page to be opened");
                await (Task)type.GetMethod("StopAsync")!.Invoke(session, null)!;
            }
        }
        finally { Call("ReleasePreview"); await (Task)type.GetMethod("StopAsync")!.Invoke(session, null)!; }
    }

    private static async Task VerifyExtrasUiAsync(PageMultiplayerExtras page, Grid grid)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierSession")!;
        var session = type.GetProperty("Current")!.GetValue(null)!;
        var room = new MCTierRoom("BCCL-other-features-QA", "", MCTierRoom.DefaultNode);
        using var host = new MCTierExtrasHub(room, "Host", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
        using var guest = new MCTierExtrasHub(room, "Guest", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
        void Refresh() => typeof(PageMultiplayerExtras).GetMethod("RefreshHub", hidden)!.Invoke(page, null);
        try
        {
            type.GetProperty("Extras")!.SetValue(session, host);
            type.GetProperty("Running")!.SetValue(session, true);
            type.GetProperty("Request")!.SetValue(session, new MCTierStartRequest(room, "Host", false, 32001, 32002, 25565));
            Refresh();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await guest.ConnectDirectAsync("127.0.0.1", host.ListeningPort, timeout.Token);
            while (host.Peers.Count != 1) await Task.Delay(25, timeout.Token);
            await guest.SendAsync("10.1.1.1", new ExtrasPacket("chat", Text: "聊天功能测试"));
            while (!((TextBlock)page.FindName("LabChat")).Text.Contains("聊天功能测试")) await Task.Delay(25, timeout.Token);
            Check(((TabControl)page.FindName("Tabs")).Items.Count == 4, "all five requested features appear in four readable tabs");
            Check(((TextBlock)page.FindName("LabChat")).Text.Contains("Guest"), "authenticated peer chat reaches the actual page");
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(32, 16, 96, 96, PixelFormats.Bgr32, null, new byte[32 * 16 * 4], 32 * 4)));
            using var picture = new MemoryStream(); encoder.Save(picture);
            await guest.SendAsync("10.1.1.1", new ExtrasPacket("screen", Size: 1920, Offset: 1080, Data: picture.ToArray()));
            while (((System.Windows.Controls.Image)page.FindName("RemoteImage")).Source is null) await Task.Delay(25, timeout.Token);
            Check(((TextBlock)page.FindName("LabScreen")).Text.Contains("Guest"), "received JPEG screen frames decode and display on the actual page");
            var burstData = picture.ToArray();
            for (var index = 0; index < 120; index++)
                await guest.BroadcastScreenAsync(new ExtrasPacket("screen", Size: 1920 + index, Offset: 1080, Data: burstData));
            while (!((TextBlock)page.FindName("LabScreen")).Text.Contains("2039")) await Task.Delay(25, timeout.Token);
            Check(true, "rapid screen updates coalesce and display the newest frame without blocking the UI");
            await guest.SendAsync("10.1.1.1", new ExtrasPacket("screen-stop"));
            while (((System.Windows.Controls.Image)page.FindName("RemoteImage")).Source is not null) await Task.Delay(25, timeout.Token);
            Check(true, "stopping screen sharing clears the viewer");
            Check(UpdateManager.BcclVersion == "0.1.0-beta" && UpdateManager.IsCurrentVersionBeta, "launcher version and update channel identify Beta 0.1.0");
            foreach (var width in new[] { 740d, 1020d, 1260d })
            {
                grid.ColumnDefinitions[0].Width = GridLength.Auto;
                grid.Measure(new Size(width, 620)); grid.Arrange(new Rect(0, 0, width, 620)); grid.UpdateLayout();
                Check(page.ActualWidth >= width - 181, $"other-functions page keeps usable space at {width}px");
            }
            var desktop = typeof(PageMultiplayerExtras).GetField("desktop", hidden)!.GetValue(page)!;
            var desktopType = desktop.GetType();
            var apply = desktopType.GetMethod("Apply")!;
            Check(!(bool)apply.Invoke(desktop, ["10.1.1.2", new ExtrasPacket("input", new string('a', 64), "move", X: .5, Y: .5)])!, "unsolicited remote input is rejected without touching the desktop");
            desktopType.GetProperty("Sharing")!.SetValue(desktop, true);
            var grant = (string)desktopType.GetMethod("Allow")!.Invoke(desktop, ["10.1.1.2"])!;
            Check(grant.Length == 64 && desktopType.GetProperty("Controller")!.GetValue(desktop)!.Equals("10.1.1.2"), "explicit consent issues a revocable token and registers the emergency hotkey");
            desktopType.GetMethod("Revoke")!.Invoke(desktop, null);
            Check(!(bool)apply.Invoke(desktop, ["10.1.1.2", new ExtrasPacket("input", grant, "move", X: .5, Y: .5)])!, "revoked remote-control tokens cannot apply input");
            typeof(PageMultiplayerExtras).GetMethod("StopActive", hidden)!.Invoke(page, null);
            Check(!(bool)desktopType.GetProperty("Sharing")!.GetValue(desktop)!, "leaving the page stops screen sharing and remote control");
        }
        finally
        {
            type.GetProperty("Extras")!.SetValue(session, null); type.GetProperty("Running")!.SetValue(session, false); type.GetProperty("Request")!.SetValue(session, null); Refresh();
        }
    }

    private static async Task VerifyNameEntryAsync(PageMultiplayer page)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string method, params object[] arguments) => typeof(PageMultiplayer).GetMethod(method, hidden)!.Invoke(page, arguments);
        var mode = typeof(PageMultiplayer).GetNestedType("EntryMode", BindingFlags.NonPublic)!;
        var nameInput = (MyTextBox)page.FindName("TextRoom");
        var status = (TextBlock)page.FindName("LabNameStatus");
        var field = typeof(PageMultiplayer).GetField("previewLease", hidden)!;
        typeof(PageMultiplayer).GetField("pageActive", hidden)!.SetValue(page, true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        async Task Wait(Func<bool> predicate)
        {
            while (!predicate()) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(100, timeout.Token); }
        }
        var endpoint = MCTierDirectoryLease.ValidateEndpoint(MCTierDirectoryLease.DefaultEndpoint);
        var room = new MCTierRoom("BCCL-typing-QA-" + Guid.NewGuid().ToString("N"), "", MCTierRoom.DefaultNode);
        await using var occupied = await MCTierDirectoryLease.ReserveAsync(room, "BCCL-QA-Occupied", endpoint, timeout.Token);
        try
        {
            Call("ShowEntry", Enum.Parse(mode, "Create"));
            nameInput.Text = room.Name;
            Call("Start", true);
            await Wait(() => !(bool)typeof(PageMultiplayer).GetField("busy", hidden)!.GetValue(page)!);
            await Wait(() => status.Text.Contains("已被使用"));
            if (((CancellationTokenSource)typeof(PageMultiplayer).GetField("nameCheck", hidden)!.GetValue(page)!).IsCancellationRequested)
                throw new Exception("Create cancelled its matching in-flight name check");
            if (!((TaskCompletionSource<bool>)typeof(PageMultiplayer).GetField("expediteNameCheck", hidden)!.GetValue(page)!).Task.IsCompletedSuccessfully)
                throw new Exception("Create did not bypass the typing debounce");
            Console.WriteLine("PASS: clicking Create immediately expedites and reuses the real in-flight check; an occupied name is rejected before any helper/elevation launch");
            Console.WriteLine("PASS: typing an occupied name displays the duplicate warning automatically");
            nameInput.Text = room.Name + "-free";
            await Wait(() => field.GetValue(page) is MCTierDirectoryLease lease && lease.Room.Name == nameInput.Text && lease.IsAlive);
            Console.WriteLine("PASS: typing a free name retains its actual reservation and displays availability");
            nameInput.Text = room.Name + "-cancelled";
            nameInput.Text = room.Name + "-latest";
            await Wait(() => field.GetValue(page) is MCTierDirectoryLease lease && lease.Room.Name == nameInput.Text && lease.IsAlive);
            Console.WriteLine("PASS: rapidly changing the name cancels stale checks and reserves only the latest value");
            var releasedRoom = ((MCTierDirectoryLease)field.GetValue(page)!).Room;
            Call("ShowEntry", Enum.Parse(mode, "Home"));
            await (Task)typeof(PageMultiplayer).GetField("previewRelease", hidden)!.GetValue(page)!;
            using var reused = await MCTierDirectoryLease.ReserveAsync(releasedRoom, "BCCL-QA-Reuse", endpoint, timeout.Token);
            Console.WriteLine("PASS: leaving the creation form releases the temporary reservation");
        }
        finally
        {
            Call("ReleasePreview");
            await (Task)typeof(PageMultiplayer).GetField("previewRelease", hidden)!.GetValue(page)!;
        }
    }

    private static void VerifyUi(PageMultiplayer page, PageMultiplayerLeft left, Grid grid)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        T Control<T>(string name) => (T)page.FindName(name);
        object? Call(string name, params object[] arguments) => typeof(PageMultiplayer).GetMethod(name, hidden)!.Invoke(page, arguments);
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            Console.WriteLine("PASS: " + label);
        }
        void Entry(string name)
        {
            var mode = typeof(PageMultiplayer).GetNestedType("EntryMode", BindingFlags.NonPublic)!;
            Call("ShowEntry", Enum.Parse(mode, name));
        }
        void CheckLayout()
        {
            foreach (var width in new[] { 740d, 900d, 1260d })
            {
                // An Auto context column reproduces the real launcher's layout contract.
                grid.ColumnDefinitions[0].Width = GridLength.Auto;
                grid.Measure(new Size(width, 450));
                grid.Arrange(new Rect(0, 0, width, 450));
                grid.UpdateLayout();
                Check(left.ActualWidth == 180 && page.ActualWidth >= width - 180 - 1, $"{width}px layout keeps a 180px sidebar and usable content");
            }
        }
        Check(Control<MyCard>("CardHome").Visibility == Visibility.Visible && Control<MyCard>("CardEntry").Visibility == Visibility.Collapsed, "initial view offers only create/join choices");
        CheckLayout();
        Entry("Create");
        Check(Control<PasswordBox>("TextPassword").MaxLength == 0 && Control<TextBlock>("LabEntryHelp").Text.Contains("管理员"), "create form has unrestricted password input and explains administrator authorization");
        Check(Control<StackPanel>("PanHostPort").Visibility == Visibility.Visible && Control<StackPanel>("PanJoin").Visibility == Visibility.Collapsed, "create view shows the local game port");
        CheckLayout();
        var generated = (MCTierRoom)Call("PrepareRoom", true)!;
        Check(generated.Name.StartsWith("BCCL-") && generated.Password.Length == 0, "blank create name is generated and blank password permits name-only joining");
        Entry("Join");
        Check(Control<Expander>("ExpRoom").Visibility == Visibility.Visible && !Control<Expander>("ExpRoom").IsExpanded &&
            Control<MyTextBox>("TextRoom").Visibility == Visibility.Visible && Control<StackPanel>("PanHostPort").Visibility == Visibility.Collapsed,
            "join exposes the room-name field and keeps invitation entry available");
        var fixture = new MCTierRoom("朋友的世界", "FixturePassword123", "tcp://127.0.0.1:11010");
        Control<MyTextBox>("TextInvite").Text = fixture.ToInvite();
        Check(fixture == (MCTierRoom)Call("PrepareRoom", false)!, "join parses the invitation directly without a separate import step");
        Control<MyTextBox>("TextInvite").Text = fixture.ToInvite() + "&bcclNetwork=direct";
        Call("PrepareRoom", false);
        Check(Control<CheckBox>("CheckDirectory").IsChecked == false, "direct-network invitations preserve legacy engine behavior");
        Control<MyTextBox>("TextInvite").Text = fixture.ToInvite() + "&bcclNetwork=managed&signal=wss%3A%2F%2Fexample.com%2Fsignaling";
        Call("PrepareRoom", false);
        Check(Control<CheckBox>("CheckDirectory").IsChecked == true && Control<MyTextBox>("TextDirectory").Text == "wss://example.com/signaling", "managed invitations restore their room-name service");
        Control<MyTextBox>("TextInvite").Text = "";
        Control<MyTextBox>("TextRoom").Text = "ManualRoom";
        Check(Control<MyTextBox>("TextInvite").Text == "", "editing the room name clears a stale pasted invitation");
        Control<PasswordBox>("TextPassword").Password = "";
        Check(((MCTierRoom)Call("PrepareRoom", false)!).Name == "ManualRoom", "manual passwordless joining remains available");
        Control<MyTextBox>("TextLocalPortSetting").Text = "34567";
        Control<MyTextBox>("TextLocalPortSetting").GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
        Check(Control<MyTextBox>("TextHostPort").Text == "34567", "advanced local-port setting uses the same engine input");
        Control<MyTextBox>("TextInvite").Text = "invalid-invite";
        try { Call("PrepareRoom", false); throw new Exception("Invalid invite was accepted"); }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { Console.WriteLine("PASS: invalid invitation is rejected before starting the engine"); }
        Call("Notice", "邀请内容有误");
        Check(Control<Border>("PanNotice").Visibility == Visibility.Visible, "errors have an inline notice surface");
        Entry("Home");
        Check(Control<MyCard>("CardGame").Visibility == Visibility.Collapsed && Control<MyTextBox>("TextRemoteIp") is not null && Control<MyComboBox>("ComboMode").Items.Count == 2, "game address, both network modes and connected controls remain present");
        var sessionType = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierSession")!;
        var session = sessionType.GetProperty("Current")!.GetValue(null)!;
        void SessionValue(string name, object? value) => sessionType.GetProperty(name)!.SetValue(session, value);
        try
        {
            SessionValue("Request", new MCTierStartRequest(fixture, "TestPlayer", false, 32001, 32002, 25565));
            SessionValue("Running", true);
            SessionValue("Peers", new MCTierPeer[] { new("Friend", "10.82.1.1", "直连", "12", false) });
            Call("UpdateSession");
            Check(Control<StackPanel>("PanForm").Visibility == Visibility.Collapsed && Control<MyCard>("CardSession").Visibility == Visibility.Visible && Control<MyCard>("CardGame").Visibility == Visibility.Visible, "connected view replaces setup with invitation/member/game controls");
            Check(Control<MyTextBox>("TextRemoteIp").Text == "10.82.1.1", "single available host is selected automatically");
            Control<MyTextBox>("TextRemoteIp").Text = "10.82.1.9";
            SessionValue("Peers", new MCTierPeer[] { new("Friend", "10.82.1.1", "直连", "15", false) });
            Call("UpdateSession");
            Check(Control<MyTextBox>("TextRemoteIp").Text == "10.82.1.9", "member metric refresh preserves a manually entered target");
            typeof(PageMultiplayer).GetField("hosting", hidden)!.SetValue(page, true);
            Call("UpdateSession");
            Check(Control<TextBlock>("LabSessionHelp").Text.Contains("25565"), "host is told which game port to share");
        }
        finally
        {
            SessionValue("Running", false); SessionValue("Peers", Array.Empty<MCTierPeer>()); SessionValue("Request", null);
            Call("UpdateSession");
        }
    }

    private static async Task DirectoryAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var endpoint = MCTierDirectoryLease.ValidateEndpoint(MCTierDirectoryLease.DefaultEndpoint);
        var room = new MCTierRoom("BCCL-QA-" + Guid.NewGuid().ToString("N"), "FixturePassword123", MCTierRoom.DefaultNode);
        MCTierDirectoryLease? preview = null, host = null, joiner = null;
        try
        {
            preview = await MCTierDirectoryLease.ReserveAsync(room, "BCCL-QA-Host", endpoint, timeout.Token);
            Console.WriteLine("PASS: official service accepts signed v3 room-name reservation");
            try
            {
                using var duplicate = await MCTierDirectoryLease.ReserveAsync(room with { Password = "DifferentPassword123" }, "BCCL-QA-Duplicate", endpoint, timeout.Token);
                throw new Exception("Duplicate name was accepted");
            }
            catch (MCTierDirectoryException ex) when (ex.NameOccupied) { Console.WriteLine("PASS: duplicate creation is rejected even with a different password"); }
            host = await preview.ConfirmAddressAsync("BCCL-QA-Host", "10.126.126.1", timeout.Token);
            preview.Dispose(); preview = null;
            Console.WriteLine("PASS: preview transfers to the real virtual address without losing its reservation");
            joiner = await MCTierDirectoryLease.JoinAsync(room, "BCCL-QA-Joiner", endpoint, "10.126.126.2", timeout.Token);
            Console.WriteLine("PASS: room name and password admit a second member without an invitation");
            host.Dispose(); host = null;
            joiner.Dispose(); joiner = null;
            await Task.Delay(1000, timeout.Token);
            using var reused = await MCTierDirectoryLease.ReserveAsync(room, "BCCL-QA-Reuse", endpoint, timeout.Token);
            Console.WriteLine("PASS: after the last member leaves the name can be used again");
        }
        finally { preview?.Dispose(); host?.Dispose(); joiner?.Dispose(); }
    }

    private static async Task ManagedSessionAsync()
    {
        var type = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierSession")!;
        var session = type.GetProperty("Current")!.GetValue(null)!;
        var start = type.GetMethod("StartRegisteredAsync")!;
        var stop = type.GetMethod("StopAsync")!;
        var room = new MCTierRoom("BCCL-session-QA-" + Guid.NewGuid().ToString("N"), "", MCTierRoom.DefaultNode);
        var endpoint = MCTierDirectoryLease.ValidateEndpoint(MCTierDirectoryLease.DefaultEndpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        try
        {
            await (Task)start.Invoke(session, [room, "BCCL-QA-Host", false, 25565, null!, true, endpoint, timeout.Token])!;
            var request = (MCTierStartRequest)type.GetProperty("Request")!.GetValue(session)!;
            var peers = await MCTierEngine.ReadPeersAsync(request.RpcPort, timeout.Token);
            if (!request.ManagedRoom || !peers.Any(p => p.IsLocal && p.Ip.StartsWith("10.126.126."))) throw new Exception("Managed room did not get a registered EasyTier address");
            Console.WriteLine("PASS: actual helper/core obtains a virtual IP and confirms the reserved name on the official service");
            using var guest = await MCTierDirectoryLease.JoinAsync(room, "BCCL-QA-Guest", endpoint, "10.126.126.254", timeout.Token);
            Console.WriteLine("PASS: passwordless room admits a member by room name alone");
            try
            {
                using var duplicate = await MCTierDirectoryLease.ReserveAsync(room, "BCCL-QA-Duplicate", endpoint, timeout.Token);
                throw new Exception("Managed room lost duplicate-name protection");
            }
            catch (MCTierDirectoryException ex) when (ex.NameOccupied) { Console.WriteLine("PASS: created session retains duplicate-name protection"); }
        }
        finally { await (Task)stop.Invoke(session, null)!; }
    }

    private static async Task SessionAsync()
    {
        var type = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierSession")!;
        var session = type.GetProperty("Current")!.GetValue(null)!;
        var start = type.GetMethod("StartAsync")!;
        var stop = type.GetMethod("StopAsync")!;
        var requestProperty = type.GetProperty("Request")!;
        var room = new MCTierRoom("BCCL-helper-test", "HelperTest123", "tcp://127.0.0.1:9");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await (Task)start.Invoke(session, [room, "TestPlayer", false, 25565, timeout.Token])!;
            var request = (MCTierStartRequest)requestProperty.GetValue(session)!;
            for (var retry = 0; ; retry++)
            {
                try
                {
                    await MCTierEngine.ReadPeersAsync(request.RpcPort, timeout.Token);
                    break;
                }
                catch (IOException) when (retry < 12) { await Task.Delay(500, timeout.Token); }
            }
            Console.WriteLine("PASS: actual BCCL helper starts with a private named pipe and its RPC responds");
            using var listenerProbe = new TcpClient();
            await listenerProbe.ConnectAsync(IPAddress.Loopback, request.SocksPort, timeout.Token);
            Console.WriteLine("PASS: loopback SOCKS portal is available");
            await (Task)stop.Invoke(session, null)!;
            using var closedProbe = new TcpClient();
            try { await closedProbe.ConnectAsync(IPAddress.Loopback, request.SocksPort, timeout.Token); throw new Exception("Engine portal remained available after disconnect"); }
            catch (SocketException) { Console.WriteLine("PASS: disconnect terminates the helper-owned core and closes its portal"); }
        }
        finally { await (Task)stop.Invoke(session, null)!; }
    }

    private static async Task FastRoomAsync()
    {
        var type = typeof(PageMultiplayer).Assembly.GetType("BCCL.Multiplayer.MCTierSession")!;
        var session = type.GetProperty("Current")!.GetValue(null)!;
        var start = type.GetMethod("StartRegisteredAsync")!;
        var stop = type.GetMethod("StopAsync")!;
        var endpoint = MCTierDirectoryLease.ValidateEndpoint(MCTierDirectoryLease.DefaultEndpoint);
        foreach (var password in new[] { "", "1", "!", "  中文 😀  ", new string('密', 1024) + "\0\n" })
        {
            var room = new MCTierRoom("BCCL-speed-QA-" + Guid.NewGuid().ToString("N"), password, MCTierRoom.DefaultNode);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var clock = Stopwatch.StartNew();
            await using var owner = await MCTierDirectoryLease.ReserveAsync(room, "BCCL-QA-Reservation", endpoint, timeout.Token);
            var reserveMs = clock.Elapsed.TotalMilliseconds;
            try
            {
                clock.Restart();
                await using var membership = await MCTierDirectoryLease.JoinAllocatedAsync(room, "BCCL-QA-Joiner", endpoint, timeout.Token);
                var admissionMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                // Join the private test reservation to exercise real fixed-address startup without accepting UAC.
                await (Task)start.Invoke(session, [room, "BCCL-QA-Joiner", false, 25565, membership, false, endpoint, timeout.Token])!;
                var request = (MCTierStartRequest)type.GetProperty("Request")!.GetValue(session)!;
                var peers = (IReadOnlyList<MCTierPeer>)type.GetProperty("Peers")!.GetValue(session)!;
                if (request.RequireAdministrator || request.VirtualIp is null || !peers.Any(p => p.IsLocal && p.Ip == request.VirtualIp)) throw new Exception("Fixed-address startup not ready");
                Console.WriteLine($"PASS: real directory reserve={reserveMs:F0}ms, admission={admissionMs:F0}ms, prepared membership/core confirmation={clock.Elapsed.TotalMilliseconds:F0}ms; password case {(password.Length == 0 ? "empty" : password.Length > 256 ? "long/control" : password == "1" ? "one digit" : password == "!" ? "punctuation" : "Unicode/spaces")}");
                // Joining through the exported invite must use the same directory and network credential.
                using var invited = await MCTierDirectoryLease.JoinAllocatedAsync(MCTierRoom.ParseInvite(room.ToInvite()), "BCCL-QA-Invite", endpoint, timeout.Token);
                if (invited.Room.NetworkPassword != room.NetworkPassword) throw new Exception("Invite changed network credentials");
            }
            finally { await (Task)stop.Invoke(session, null)!; }
        }
    }

    private static async Task VerifyAdminGuardAsync(string? executable)
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        { Console.WriteLine("SKIP: non-administrator rejection requires a non-elevated test process"); return; }
        var pipeName = "BCCL-MCTier-" + Guid.NewGuid().ToString("N");
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var parent = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(executable ?? Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--mctier-helper", pipeName, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString() }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
            {
                var request = new MCTierStartRequest(new MCTierRoom("BCCL-elevation-QA", "1", "tcp://127.0.0.1:9"),
                    "BCCL-QA", false, MCTierEngine.FreePort(), MCTierEngine.FreePort(), 25565, RequireAdministrator: true);
                await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(request));
            }
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 1) throw new Exception("Non-administrator creation was not rejected");
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            if (await reader.ReadLineAsync(timeout.Token) is not null) throw new Exception("Unauthorised helper reported a ready core");
            Console.WriteLine("PASS: a creation request is rejected by the real helper when it has no administrator token; no core is launched");
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    private static async Task PackageHelperAsync(string executable)
    {
        var folder = Path.Combine(Paths.Data, "独立包 helper with spaces");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "Boat Chris Craft Launcher.exe");
        File.Copy(executable, target, true);
        if (Directory.GetFiles(folder, "*.dll").Length > 0) throw new Exception("Adjacent DLLs invalidate the standalone test");
        var name = "BCCL-MCTier-" + Guid.NewGuid().ToString("N");
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var parent = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(target) { WorkingDirectory = Path.GetTempPath(), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "--mctier-helper", name, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString() }) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_ROOT"] = Path.Combine(folder, "no-installed-runtime");
        start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            var request = new MCTierStartRequest(new MCTierRoom("BCCL-package-QA-" + Guid.NewGuid().ToString("N"), "123", "tcp://127.0.0.1:9"),
                "BCCL-QA", false, MCTierEngine.FreePort(), MCTierEngine.FreePort(), 25565);
            await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(request));
            if (await reader.ReadLineAsync(timeout.Token) != "READY") throw new Exception("Published helper failed to start");
            for (var retry = 0; ; retry++)
            {
                try { await MCTierEngine.ReadPeersAsync(request.RpcPort, timeout.Token); break; }
                catch (IOException) when (retry < 15) { await Task.Delay(250, timeout.Token); }
            }
            Console.WriteLine("PASS: published single EXE loads its real helper/core from a Chinese/spaced folder with no adjacent DLLs or installed DOTNET_ROOT");
            writer.Dispose();
            pipe.Dispose();
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new Exception("Published helper cleanup failed");
            Console.WriteLine("PASS: closing the control pipe shuts down the packaged helper and its own engine");
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    private static async Task NetworkAsync(string source, bool screen60 = false)
    {
        Directory.CreateDirectory(MCTierRuntime.DirectoryPath);
        foreach (var name in new[] { "easytier-core.exe", "easytier-cli.exe", "Packet.dll", "WinDivert64.sys", "wintun.dll" })
            File.Copy(Path.Combine(source, name), Path.Combine(MCTierRuntime.DirectoryPath, name), true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var gamePort = ((IPEndPoint)echo.LocalEndpoint).Port;
        var listenerPort = MCTierEngine.FreePort();
        var room = new MCTierRoom("BCCL-verification-" + Guid.NewGuid().ToString("N"), "FixturePassword123", $"tcp://127.0.0.1:{listenerPort}");
        var request1 = new MCTierStartRequest(room, "Host", false, MCTierEngine.FreePort(), MCTierEngine.FreePort(), gamePort);
        var request2 = new MCTierStartRequest(room, "Guest", false, MCTierEngine.FreePort(), MCTierEngine.FreePort(), gamePort);
        using var host = MCTierEngine.Create(request1);
        using var guest = MCTierEngine.Create(request2);
        void Set(Process process, string key, string value)
        {
            var index = process.StartInfo.ArgumentList.IndexOf("--" + key);
            if (index >= 0) process.StartInfo.ArgumentList[index + 1] = value;
            else { process.StartInfo.ArgumentList.Add("--" + key); process.StartInfo.ArgumentList.Add(value); }
        }
        Set(host, "dhcp", "false"); Set(host, "ipv4", "10.82.1.1"); Set(host, "listeners", $"tcp://127.0.0.1:{listenerPort}");
        Set(host, "bind-device", "false"); Set(guest, "bind-device", "false");
        foreach (var process in new[] { host, guest })
        {
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, line) => { if (line.Data is not null) Console.WriteLine(line.Data.Replace(room.Password, "[redacted]")); };
        }
        try
        {
            host.Start(); host.BeginOutputReadLine(); host.BeginErrorReadLine();
            guest.Start(); guest.BeginOutputReadLine(); guest.BeginErrorReadLine();
            IReadOnlyList<MCTierPeer> peers = [];
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(500, token);
                if (host.HasExited || guest.HasExited) throw new Exception("Engine exited during network verification");
                try { peers = await MCTierEngine.ReadPeersAsync(request2.RpcPort, token); } catch (IOException) { continue; }
                if (peers.Any(p => p.Ip == "10.82.1.1") && peers.Any(p => p.IsLocal)) break;
            }
            Console.WriteLine("PASS: two real EasyTier instances discover each other using MCTier room identity");
            Console.WriteLine("PASS: guest receives a virtual IPv4 through DHCP without a system adapter");
            using var bridge = new MCTierForwarder(request2.SocksPort, "10.82.1.1", gamePort);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, bridge.LocalPort, token);
            var payload = Encoding.UTF8.GetBytes("BCCL Minecraft TCP bridge 数据验证");
            await client.GetStream().WriteAsync(payload, token);
            using var server = await echo.AcceptTcpClientAsync(token);
            var received = new byte[payload.Length];
            await server.GetStream().ReadExactlyAsync(received, token);
            if (!payload.SequenceEqual(received)) throw new Exception("Forwarded payload differs");
            await server.GetStream().WriteAsync(received, token);
            await client.GetStream().ReadExactlyAsync(received, token);
            if (!payload.SequenceEqual(received)) throw new Exception("Return payload differs");
            Console.WriteLine("PASS: TCP payload traverses loopback bridge → SOCKS → virtual network → host and returns intact");
            using var hostExtras = new MCTierExtrasHub(room, "Host", IPAddress.Loopback) { LocalAddress = "10.82.1.1" };
            using var guestExtras = new MCTierExtrasHub(room, "Guest", IPAddress.Loopback, 0) { LocalAddress = peers.First(p => p.IsLocal).Ip };
            hostExtras.Notice += text => Console.WriteLine("HOST: " + text);
            guestExtras.Notice += text => Console.WriteLine("GUEST: " + text);
            using var extrasBridge = new MCTierForwarder(request2.SocksPort, "10.82.1.1", MCTierExtrasHub.Port);
            var messages = new System.Collections.Concurrent.ConcurrentQueue<ExtrasPacket>();
            hostExtras.Received += (_, packet) => messages.Enqueue(packet);
            await guestExtras.ConnectDirectAsync("127.0.0.1", extrasBridge.LocalPort, token);
            while (hostExtras.Peers.Count != 1 || guestExtras.Peers.Count != 1) await Task.Delay(50, token);
            foreach (var packet in new[] { new ExtrasPacket("chat", Text: "隧道聊天"), new ExtrasPacket("voice", Data: new byte[1280]),
                new ExtrasPacket("screen", Size: 1920, Offset: 1080, Data: new byte[100000]), new ExtrasPacket("control-request"), new ExtrasPacket("control-revoke") })
                await guestExtras.SendAsync("10.82.1.1", packet);
            while (messages.Count < 5) await Task.Delay(50, token);
            Console.WriteLine("PASS: authenticated chat, voice/screen payloads and control-consent messages cross the actual EasyTier/SOCKS service port");
            var fixtureFile = Path.Combine(Paths.Data, "tunnel-file.bin");
            var fixtureBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(1048613);
            await File.WriteAllBytesAsync(fixtureFile, fixtureBytes, token);
            await hostExtras.ShareFileAsync(fixtureFile);
            while (guestExtras.Files.Count != 1) await Task.Delay(50, token);
            var receivedFolder = Path.Combine(Paths.Data, "tunnel-files-" + Guid.NewGuid().ToString("N"));
            await guestExtras.ReceiveFileAsync(guestExtras.Files[0], receivedFolder);
            while (!Directory.GetFiles(receivedFolder).Any(p => !p.EndsWith(".partial"))) await Task.Delay(50, token);
            var tunnelBytes = await File.ReadAllBytesAsync(Directory.GetFiles(receivedFolder).Single(), token);
            if (!fixtureBytes.SequenceEqual(tunnelBytes)) throw new Exception("Tunnel file differs");
            Console.WriteLine("PASS: accepted file transfers over the virtual network and passes end-to-end SHA-256 verification");
            if (screen60) await VerifyScreen60Async(hostExtras, guestExtras, token);
            bridge.Dispose();
            using var probe = new TcpClient();
            try { await probe.ConnectAsync(IPAddress.Loopback, bridge.LocalPort, token); throw new Exception("Bridge remained open"); }
            catch (SocketException) { Console.WriteLine("PASS: disconnect closes local game listener"); }
        }
        finally
        {
            echo.Stop();
            foreach (var process in new[] { host, guest })
                try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { }
        }
    }

    private static async Task VerifyScreen60Async(MCTierExtrasHub sender, MCTierExtrasHub receiver, CancellationToken token)
    {
        var assembly = typeof(PageMultiplayer).Assembly;
        var captureType = assembly.GetType("BCCL.Multiplayer.RoomScreenCapture")!;
        var streamType = assembly.GetType("BCCL.Multiplayer.RoomScreenStream")!;
        if ((int)streamType.GetField("TargetFramesPerSecond")!.GetRawConstantValue()! != 60) throw new Exception("Target is not 60 fps");
        Exception? failure = null;
        var received = 0;
        long captureTicks = 0, sendTicks = 0;
        var captureCount = 0;
        string backend = "";
        long freshFrames = 0;
        byte[]? lastFrame = null;
        receiver.Received += (_, packet) =>
        { if (packet.Kind == "screen") { Interlocked.Increment(ref received); lastFrame = packet.Data; } };
        Func<(Func<ExtrasPacket>, Action)> prepare = () =>
        {
            var capture = Activator.CreateInstance(captureType)!;
            return (() =>
            {
                var start = Stopwatch.GetTimestamp();
                var packet = (ExtrasPacket)captureType.GetMethod("Capture")!.Invoke(capture, null)!;
                backend = (string)captureType.GetProperty("Backend")!.GetValue(capture)!;
                freshFrames = (long)captureType.GetProperty("FreshFrames")!.GetValue(capture)!;
                captureTicks += Stopwatch.GetTimestamp() - start; captureCount++; return packet;
            }, () => ((IDisposable)capture).Dispose());
        };
        var clock = new Stopwatch();
        Func<ExtrasPacket, Task> transmit = async packet =>
        {
            var start = Stopwatch.GetTimestamp(); await sender.BroadcastScreenAsync(packet);
            Interlocked.Add(ref sendTicks, Stopwatch.GetTimestamp() - start);
        };
        var stream = Activator.CreateInstance(streamType, [prepare, new Func<bool>(() => true), transmit, new Action<Exception>(ex => failure = ex)])!;
        // Exclude device creation and WIC/JIT startup from the steady-state frame-rate measurement.
        while ((long)streamType.GetProperty("FramesSent")!.GetValue(stream)! < 30 && failure is null) await Task.Delay(10, token);
        var warmSent = (long)streamType.GetProperty("FramesSent")!.GetValue(stream)!;
        var warmCapture = captureCount; var warmCaptureTicks = captureTicks; var warmSendTicks = sendTicks;
        clock.Start();
        try { await Task.Delay(5000, token); }
        finally { streamType.GetMethod("Stop")!.Invoke(stream, null); }
        await ((Task)streamType.GetProperty("Completion")!.GetValue(stream)!).WaitAsync(token);
        clock.Stop();
        if (failure is not null) throw new Exception("Native capture/send failed", failure);
        var sent = (long)streamType.GetProperty("FramesSent")!.GetValue(stream)!;
        var finish = Stopwatch.StartNew();
        while (received < sent && finish.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(10, token);
        if (sent < 30 || received != sent || lastFrame is null) throw new Exception($"Screen stream did not arrive: sent={sent}, received={received}");
        if (captureCount - warmCapture < 250) throw new Exception("Native capture did not approach the configured 60 fps target on this test machine");
        using var imageData = new MemoryStream(lastFrame);
        var image = System.Windows.Media.Imaging.BitmapDecoder.Create(imageData, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        if (image.PixelWidth > 960 || image.PixelHeight > 540) throw new Exception("Screen image dimensions changed");
        var stoppedCount = received;
        await Task.Delay(250, token);
        if (received != stoppedCount) throw new Exception("Screen frames continue after stopping");
        Console.WriteLine($"PASS: native screen capture → JPEG → actual EasyTier tunnel → receive/decode: target=60 fps, total sent={sent}, received={received}, measured sent={sent - warmSent}, elapsed={clock.Elapsed.TotalSeconds:F2}s, rate={(sent - warmSent) / clock.Elapsed.TotalSeconds:F1} fps");
        Console.WriteLine($"PROFILE: backend={backend}, measured captured={captureCount - warmCapture}, fresh desktop updates={freshFrames}, capture/encode={(captureTicks - warmCaptureTicks) * 1000d / Stopwatch.Frequency / (captureCount - warmCapture):F2} ms/frame, send={(sendTicks - warmSendTicks) * 1000d / Stopwatch.Frequency / (sent - warmSent):F2} ms/frame");
        Console.WriteLine("PASS: stopping the screen stream stops frames and releases its capture resources; no screen pixels were saved to disk");

        var active = 0; var maximum = 0; var count = 0;
        Func<(Func<ExtrasPacket>, Action)> synthetic = () => (() => new ExtrasPacket("screen", Size: 32, Offset: 16, Data: [1]), () => { });
        Func<ExtrasPacket, Task> slow = async _ =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref active));
            await Task.Delay(40, token); Interlocked.Decrement(ref active); count++;
        };
        var throttled = Activator.CreateInstance(streamType, [synthetic, new Func<bool>(() => true), slow, new Action<Exception>(ex => failure = ex)])!;
        await Task.Delay(250, token); streamType.GetMethod("Stop")!.Invoke(throttled, null);
        await (Task)streamType.GetProperty("Completion")!.GetValue(throttled)!;
        if (maximum != 1 || count > 10) throw new Exception("Slow connections accumulate screen frames");
        Console.WriteLine("PASS: slow sending keeps only one frame in flight instead of accumulating parallel sends");
    }

    private static void InitializeConfig()
    {
        var config = typeof(ConfigService);
        config.GetMethod("_InitializeConfigItems", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        config.GetField("_isConfigItemsInitialized", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        foreach (var key in ConfigService.KeySet)
        {
            ConfigService.TryGetConfigItemNoType(key, out var item);
            item!.GetType().GetField("<_Provider>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(item, new MemoryProvider());
        }
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
