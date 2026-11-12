using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BCCL.Core.App;
using BCCL.Core.Link.MCTier;
using BCCL.Multiplayer;
using Microsoft.Win32;

namespace BCCL;

public partial class PageMultiplayerExtras
{
    private readonly MCTierSession session = MCTierSession.Current;
    private MCTierExtrasHub? hub;
    private Action<ExtrasPeer, ExtrasPacket>? receiver;
    private readonly RoomAudio audio = new();
    private readonly RoomDesktop desktop = new();
    private readonly System.Windows.Threading.DispatcherTimer controlExpiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private RoomScreenStream? screenStream;
    private Task screenStopping = Task.CompletedTask;
    private readonly ConcurrentDictionary<string, ExtrasPacket> pendingScreens = new();
    private int screenRefreshQueued;
    private bool decodingScreen, screenDecodeRequested;
    private long screenViewRevision;
    private readonly Stopwatch screenDisplayClock = Stopwatch.StartNew();
    private int displayedFrames;
    private double displayedFrameRate;
    private byte[]? decodedScreenData;
    private string decodedScreenPeer = "";
    private long decodedScreenRevision;
    private string memberSignature = "", fileSignature = "", pendingController = "", requestedController = "", remoteGrant = "", controlledPeer = "";
    private DateTime lastRequest;
    private bool refreshing;
    private int sendingVoice;
    private long lastMove;
    private readonly HashSet<int> remoteKeys = new();
    private readonly Dictionary<string, ExtrasPacket> screens = new();
    private string DownloadsFolder => Path.Combine(Paths.Data, "Multiplayer", "ReceivedFiles");
    private sealed record FileRow(ExtrasFile File, string Text);

    public PageMultiplayerExtras()
    {
        InitializeComponent();
        session.Changed += SessionChanged;
        Loaded += (_, _) => RefreshHub();
        Unloaded += (_, _) => StopActive();
        audio.Captured += SendVoice;
        audio.Failed += text => Dispatcher.BeginInvoke(new Action(() => { audio.StopMicrophone(); Notice(text); UpdateActivity(); }));
        desktop.Revoked += () =>
        {
            _ = hub?.BroadcastAsync(new ExtrasPacket("control-revoke"));
            Notice("已收回远程控制授权。"); UpdateActivity();
        };
        controlExpiryTimer.Tick += (_, _) => desktop.CheckExpiry();
        RefreshHub();
    }

    private void SessionChanged() => Dispatcher.BeginInvoke(new Action(RefreshHub));
    private void HubChanged() => Dispatcher.BeginInvoke(new Action(RefreshMembers));
    private void HubNotice(string text) => Dispatcher.BeginInvoke(new Action(() => Notice(text)));

    private void RefreshHub()
    {
        if (!ReferenceEquals(hub, session.Extras))
        {
            StopActive();
            if (hub is not null) { hub.Changed -= HubChanged; hub.Received -= receiver; hub.Notice -= HubNotice; }
            hub = session.Extras;
            if (hub is not null)
            {
                var origin = hub;
                receiver = (peer, packet) =>
                {
                    if (!ReferenceEquals(origin, session.Extras)) return;
                    if (packet.Kind == "screen")
                    {
                        pendingScreens[peer.Ip] = packet;
                        if (Interlocked.Exchange(ref screenRefreshQueued, 1) == 0)
                            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                            {
                                Interlocked.Exchange(ref screenRefreshQueued, 0);
                                if (!ReferenceEquals(origin, session.Extras)) return;
                                foreach (var entry in pendingScreens)
                                    if (pendingScreens.TryRemove(entry.Key, out var latest)) screens[entry.Key] = latest;
                                ShowScreen();
                            }));
                    }
                    else
                    {
                        if (packet.Kind is "screen-stop" or "disconnected") pendingScreens.TryRemove(peer.Ip, out _);
                        Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(origin, session.Extras)) Receive(peer, packet); }));
                    }
                };
                hub.Changed += HubChanged; hub.Received += receiver; hub.Notice += HubNotice;
            }
            pendingScreens.Clear(); screens.Clear(); screenViewRevision++; decodedScreenData = null; RemoteImage.Source = null;
            PanConsent.Visibility = Visibility.Collapsed;
            memberSignature = fileSignature = "";
            LabShared.Text = "尚未分享文件。";
        }
        LabConnection.Text = session.Running
            ? $"当前大厅：{session.Request?.Room.Name} · {hub?.Peers.Count ?? 0} 位朋友已连接其他功能" + (session.ExtrasError.Length > 0 ? "\n" + session.ExtrasError : "")
            : "请先到联机大厅创建或加入房间。";
        RefreshMembers();
    }

    private void RefreshMembers()
    {
        var peers = hub?.Peers ?? [];
        var signature = string.Join("|", peers.OrderBy(p => p.Ip).Select(p => p.ToString()));
        if (signature != memberSignature)
        {
            var selected = (ComboScreens.SelectedItem as ExtrasPeer)?.Ip;
            refreshing = true;
            ComboScreens.ItemsSource = peers;
            ComboScreens.SelectedItem = peers.FirstOrDefault(p => p.Ip == selected) ?? peers.FirstOrDefault();
            refreshing = false;
            memberSignature = signature;
            ShowScreen();
        }
        var files = hub?.Files ?? [];
        var nextFiles = string.Join("|", files.Select(f => f.PeerIp + f.Id).Order());
        if (nextFiles != fileSignature)
        {
            var selected = (ListFiles.SelectedItem as FileRow)?.File;
            var rows = files.Select(f => new FileRow(f, $"{f.Name} · {f.Size / 1024d:N1} KB · {peers.FirstOrDefault(p => p.Ip == f.PeerIp)?.Name ?? f.PeerIp}")).ToArray();
            ListFiles.ItemsSource = rows;
            ListFiles.SelectedItem = rows.FirstOrDefault(r => r.File == selected);
            fileSignature = nextFiles;
        }
        if (session.Running) LabConnection.Text = $"当前大厅：{session.Request?.Room.Name} · {peers.Count} 位朋友已连接其他功能" + (session.ExtrasError.Length > 0 ? "\n" + session.ExtrasError : "");
    }

    private void Notice(string text) => LabNotice.Text = text;
    private void AddChat(string name, string text)
    {
        LabChat.Text += $"{DateTime.Now:HH:mm}  {name}\n{text}\n\n";
        if (LabChat.Text.Length > 100000) LabChat.Text = LabChat.Text[^80000..];
        ChatScroll.ScrollToEnd();
    }

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Notice(ex.Message); }
    }

    private MCTierExtrasHub RequireHub() => hub ?? throw new InvalidOperationException("请先创建或加入大厅。");
    private async Task SendChat()
    {
        var current = RequireHub();
        var text = TextChat.Text.Trim();
        if (text.Length == 0) return;
        if (current.Peers.Count == 0) throw new InvalidOperationException("等待朋友连接其他功能后再发送消息。");
        await current.BroadcastAsync(new ExtrasPacket("chat", Text: text));
        AddChat("我", text); TextChat.Text = "";
    }
    private async void SendChat_Click(object sender, MouseButtonEventArgs e) => await Run(SendChat);
    private async void Chat_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Run(SendChat); } }

    private async void ShareFile_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        var current = RequireHub();
        var dialog = new OpenFileDialog { Title = "选择要分享给大厅成员的文件", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var file in dialog.FileNames) await current.ShareFileAsync(file);
        LabShared.Text = "已分享：" + string.Join("、", dialog.SafeFileNames) + "。朋友连接后也能看到这些文件。";
        Notice("文件已加入分享列表，朋友点击接收后才会传输。");
    });
    private async void ReceiveFile_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        if (ListFiles.SelectedItem is not FileRow row) throw new InvalidOperationException("请先选择朋友分享的文件。");
        await RequireHub().ReceiveFileAsync(row.File, DownloadsFolder);
        Notice("正在接收 " + row.File.Name + "，完成后会在这里显示保存位置。退出大厅会取消接收。");
    });
    private void OpenDownloads_Click(object sender, MouseButtonEventArgs e)
    {
        try { Directory.CreateDirectory(DownloadsFolder); Process.Start(new ProcessStartInfo(DownloadsFolder) { UseShellExecute = true }); }
        catch (Exception ex) { Notice(ex.Message); }
    }
    private async void ClearShared_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        await RequireHub().ClearSharedAsync(); LabShared.Text = "尚未分享文件。"; Notice("已停止分享所有文件。");
    });
    private async void CancelReceiving_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        await RequireHub().CancelReceivingAsync(); Notice("已取消全部文件接收。");
    });

    private void Listen_Click(object sender, MouseButtonEventArgs e)
    {
        if (hub is null) { Notice("请先创建或加入大厅。"); return; }
        if (audio.Listening) { audio.StopMicrophone(); audio.MutePlayback(); }
        else { audio.Listening = true; Notice("已开启语音收听。需要说话时再开启麦克风。"); }
        UpdateActivity();
    }
    private void Microphone_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            RequireHub();
            if (audio.MicrophoneOn) audio.StopMicrophone();
            else { audio.Listening = true; audio.StartMicrophone(); Notice("麦克风已开启，声音将发送给大厅内已连接的 BCCL 成员。"); }
        }
        catch (Exception ex) { Notice(ex.Message); }
        UpdateActivity();
    }
    private async void SendVoice(byte[] data)
    {
        if (Interlocked.Exchange(ref sendingVoice, 1) != 0) return;
        try { var current = hub; if (current is not null) await current.BroadcastAsync(new ExtrasPacket("voice", Data: data)); }
        finally { Interlocked.Exchange(ref sendingVoice, 0); }
    }

    private async void ShareScreen_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        var intendedRoom = RequireHub();
        if (desktop.Sharing) await StopScreen();
        else
        {
            await screenStopping;
            if (!IsLoaded || desktop.Sharing || !ReferenceEquals(intendedRoom, hub)) return;
            var current = intendedRoom;
            desktop.Sharing = true; controlExpiryTimer.Start();
            RoomScreenStream? next = null;
            next = new RoomScreenStream(() =>
            {
                var source = new RoomScreenCapture(); return (source.Capture, source.Dispose);
            }, () => current.Peers.Count > 0, current.BroadcastScreenAsync, ex => Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!ReferenceEquals(screenStream, next)) return;
                await StopScreen(); Notice("屏幕共享已停止：" + ex.Message);
            })));
            screenStream = next;
            Notice("正在以 60 帧/秒为目标共享主显示器。大厅内已连接的 BCCL 成员可以看到屏幕内容。"); UpdateActivity();
        }
    });
    private Task StopScreen()
    {
        desktop.Sharing = false; controlExpiryTimer.Stop(); desktop.Revoke();
        var previous = screenStopping;
        var stream = screenStream; screenStream = null; stream?.Stop();
        var current = hub;
        PanConsent.Visibility = Visibility.Collapsed; pendingController = "";
        UpdateActivity();
        return screenStopping = FinishStoppingScreen(previous, stream, current);
    }
    private async Task FinishStoppingScreen(Task previous, RoomScreenStream? stream, MCTierExtrasHub? current)
    {
        await previous;
        if (stream is not null) await stream.Completion;
        if (!desktop.Sharing && current is not null && ReferenceEquals(hub, current))
            await current.BroadcastAsync(new ExtrasPacket("screen-stop"));
    }

    private void Receive(ExtrasPeer peer, ExtrasPacket packet)
    {
        try
        {
            switch (packet.Kind)
            {
                case "chat": AddChat(peer.Name, packet.Text); break;
                case "voice": if (IsLoaded) audio.Play(peer.Ip, packet.Data!); break;
                case "screen": screens[peer.Ip] = packet; if ((ComboScreens.SelectedItem as ExtrasPeer)?.Ip == peer.Ip) ShowScreen(); break;
                case "screen-stop": screenViewRevision++; screens.Remove(peer.Ip); if (controlledPeer == peer.Ip) StopControlling(); ShowScreen(); break;
                case "control-request":
                    if (!IsLoaded || !desktop.Sharing || DateTime.UtcNow - lastRequest < TimeSpan.FromSeconds(5)) { _ = hub?.SendAsync(peer.Ip, new ExtrasPacket("control-deny")); break; }
                    pendingController = peer.Ip; lastRequest = DateTime.UtcNow;
                    LabConsent.Text = peer.Name + " 请求控制你的电脑。是否允许？"; PanConsent.Visibility = Visibility.Visible; break;
                case "control-grant":
                    if (IsLoaded && requestedController == peer.Ip)
                    { remoteGrant = packet.Id; controlledPeer = peer.Ip; Notice("对方已允许控制。点击对方画面后可使用鼠标和键盘。"); }
                    break;
                case "control-deny": if (requestedController == peer.Ip) { StopControlling(); Notice("对方未允许远程控制。"); } break;
                case "control-revoke":
                    if (desktop.Controller == peer.Ip) desktop.Revoke();
                    if (controlledPeer == peer.Ip || requestedController == peer.Ip) { StopControlling(false); Notice("远程控制已结束。"); }
                    break;
                case "input": desktop.Apply(peer.Ip, packet); break;
                case "disconnected":
                    screenViewRevision++;
                    audio.Remove(peer.Ip); screens.Remove(peer.Ip);
                    if (desktop.Controller == peer.Ip) desktop.Revoke();
                    if (controlledPeer == peer.Ip || requestedController == peer.Ip) StopControlling(false);
                    if (pendingController == peer.Ip) { pendingController = ""; PanConsent.Visibility = Visibility.Collapsed; }
                    ShowScreen(); break;
            }
        }
        catch (Exception ex) { audio.MutePlayback(); Notice("功能暂时不可用：" + ex.Message); UpdateActivity(); }
    }

    private void ScreenPeer_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        screenViewRevision++;
        StopControlling(); ShowScreen();
    }
    private async void ShowScreen()
    {
        var peer = ComboScreens.SelectedItem as ExtrasPeer;
        if (peer is null || !screens.TryGetValue(peer.Ip, out var screen))
        {
            RemoteImage.Source = null; LabScreen.Text = "等待所选成员主动开启屏幕共享。";
            decodedScreenData = null;
            displayedFrames = 0; displayedFrameRate = 0; screenDisplayClock.Restart(); return;
        }
        if (ReferenceEquals(decodedScreenData, screen.Data) && decodedScreenPeer == peer.Ip && decodedScreenRevision == screenViewRevision && RemoteImage.Source is not null)
        { UpdateScreenRate(peer, screen); return; }
        if (decodingScreen) { screenDecodeRequested = true; return; }
        decodingScreen = true;
        var revision = screenViewRevision;
        try
        {
            var image = await Task.Run(() => DecodeScreen(screen));
            if (revision == screenViewRevision && (ComboScreens.SelectedItem as ExtrasPeer)?.Ip == peer.Ip && screens.ContainsKey(peer.Ip))
            {
                RemoteImage.Source = image;
                decodedScreenData = screen.Data; decodedScreenPeer = peer.Ip; decodedScreenRevision = revision;
                UpdateScreenRate(peer, screen);
            }
        }
        catch (Exception ex) { Notice("无法显示屏幕画面：" + ex.Message); }
        finally
        {
            decodingScreen = false;
            if (screenDecodeRequested) { screenDecodeRequested = false; ShowScreen(); }
        }
    }
    private void UpdateScreenRate(ExtrasPeer peer, ExtrasPacket screen)
    {
        displayedFrames++;
        if (screenDisplayClock.Elapsed.TotalSeconds >= 1)
        { displayedFrameRate = displayedFrames / screenDisplayClock.Elapsed.TotalSeconds; displayedFrames = 0; screenDisplayClock.Restart(); }
        var rate = displayedFrameRate > 0 ? $"约 {displayedFrameRate:F0} 帧/秒" : "目标 60 帧/秒";
        LabScreen.Text = $"{peer.Name} 的屏幕 · {screen.Size} × {screen.Offset} · {rate}";
    }
    private static BitmapImage DecodeScreen(ExtrasPacket screen)
    {
        using var stream = new MemoryStream(screen.Data!);
        var metadata = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        if (metadata.PixelWidth is < 1 or > 16384 || metadata.PixelHeight is < 1 or > 16384 || (long)metadata.PixelWidth * metadata.PixelHeight > 100_000_000)
            throw new IOException("屏幕画面尺寸无效。");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit();
        image.DecodePixelWidth = Math.Max(1, (int)(metadata.PixelWidth * Math.Min(1d, Math.Min(960d / metadata.PixelWidth, 540d / metadata.PixelHeight))));
        image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
    private async void RequestControl_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        var peer = ComboScreens.SelectedItem as ExtrasPeer ?? throw new InvalidOperationException("请先选择一位成员。");
        if (!screens.ContainsKey(peer.Ip)) throw new InvalidOperationException("请先让对方开启屏幕共享。");
        StopControlling(); requestedController = peer.Ip;
        await RequireHub().SendAsync(peer.Ip, new ExtrasPacket("control-request")); Notice("已发送请求，等待对方明确同意。");
    });
    private async void Allow_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        if (pendingController.Length == 0) return;
        var ip = pendingController;
        var token = desktop.Allow(ip);
        try { await RequireHub().SendAsync(ip, new ExtrasPacket("control-grant", token)); }
        catch { desktop.Revoke(); throw; }
        pendingController = ""; PanConsent.Visibility = Visibility.Collapsed;
        Notice("远程控制已允许。按 Ctrl+Alt+F12 可立即停止。"); UpdateActivity();
    });
    private async void Deny_Click(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        var ip = pendingController; pendingController = ""; PanConsent.Visibility = Visibility.Collapsed;
        if (ip.Length > 0) await RequireHub().SendAsync(ip, new ExtrasPacket("control-deny"));
    });
    private void Revoke_Click(object sender, MouseButtonEventArgs e) => desktop.Revoke();
    private void StopControl_Click(object sender, MouseButtonEventArgs e) { StopControlling(); Notice("已停止控制对方。"); }

    private void StopControlling(bool notify = true)
    {
        var ip = controlledPeer.Length > 0 ? controlledPeer : requestedController;
        if (remoteGrant.Length > 0)
            foreach (var key in remoteKeys.ToArray()) _ = SendInputPacket("key-up", key);
        remoteKeys.Clear(); RemoteImage.ReleaseMouseCapture();
        controlledPeer = requestedController = remoteGrant = "";
        if (notify && ip.Length > 0) _ = Run(async () => { if (hub is not null) await hub.SendAsync(ip, new ExtrasPacket("control-revoke")); });
    }

    private async Task SendInputPacket(string action, int code = 0, Point? position = null)
    {
        if (hub is null || controlledPeer.Length == 0 || remoteGrant.Length == 0) return;
        var point = position ?? new Point(0.5, 0.5);
        try { await hub.SendAsync(controlledPeer, new ExtrasPacket("input", remoteGrant, action, X: point.X, Y: point.Y, Code: code)); }
        catch (Exception ex) { StopControlling(false); Notice("控制连接已断开：" + ex.Message); }
    }
    private Point? ScreenPosition(MouseEventArgs e)
    {
        if (RemoteImage.Source is not BitmapSource image) return null;
        var scale = Math.Min(RemoteImage.ActualWidth / image.PixelWidth, RemoteImage.ActualHeight / image.PixelHeight);
        if (scale <= 0) return null;
        var width = image.PixelWidth * scale; var height = image.PixelHeight * scale;
        var position = e.GetPosition(RemoteImage);
        var x = (position.X - (RemoteImage.ActualWidth - width) / 2) / width;
        var y = (position.Y - (RemoteImage.ActualHeight - height) / 2) / height;
        return x is >= 0 and <= 1 && y is >= 0 and <= 1 ? new Point(x, y) : null;
    }
    private async void Remote_MouseMove(object sender, MouseEventArgs e)
    {
        if (Environment.TickCount64 - lastMove < 50 || ScreenPosition(e) is not Point point) return;
        lastMove = Environment.TickCount64; await SendInputPacket("move", position: point);
    }
    private async void Remote_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ScreenPosition(e) is not Point point || controlledPeer.Length == 0 || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        RemoteImage.Focus(); RemoteImage.CaptureMouse(); e.Handled = true;
        await SendInputPacket(e.ChangedButton == MouseButton.Left ? "left-down" : "right-down", position: point);
    }
    private async void Remote_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (controlledPeer.Length == 0 || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        await SendInputPacket(e.ChangedButton == MouseButton.Left ? "left-up" : "right-up", position: ScreenPosition(e));
        RemoteImage.ReleaseMouseCapture(); e.Handled = true;
    }
    private async void Remote_MouseWheel(object sender, MouseWheelEventArgs e)
    { if (ScreenPosition(e) is Point point && controlledPeer.Length > 0) { await SendInputPacket("wheel", e.Delta, point); e.Handled = true; } }
    private async void Remote_KeyDown(object sender, KeyEventArgs e)
    {
        if (controlledPeer.Length == 0) return;
        var key = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        remoteKeys.Add(key); await SendInputPacket("key-down", key); e.Handled = true;
    }
    private async void Remote_KeyUp(object sender, KeyEventArgs e)
    {
        if (controlledPeer.Length == 0) return;
        var key = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        remoteKeys.Remove(key); await SendInputPacket("key-up", key); e.Handled = true;
    }
    private void Remote_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    { foreach (var key in remoteKeys.ToArray()) _ = SendInputPacket("key-up", key); remoteKeys.Clear(); }

    private void UpdateActivity()
    {
        BtnListen.Text = audio.Listening ? "关闭收听" : "开启收听";
        BtnMicrophone.Text = audio.MicrophoneOn ? "关闭麦克风" : "开启麦克风";
        BtnScreen.Text = desktop.Sharing ? "停止共享我的屏幕" : "开始共享我的屏幕";
        LabActivity.Text = (audio.MicrophoneOn ? "麦克风开启" : "麦克风关闭") + " · " + (desktop.Sharing ? "屏幕共享中" : "屏幕共享关闭") + " · " +
            (desktop.Controller.Length > 0 ? "正在接受远程控制（Ctrl+Alt+F12 停止）" : "无远程控制授权");
    }
    private void StopActive()
    {
        audio.Dispose(); StopControlling();
        _ = StopScreen(); UpdateActivity();
    }
    private void StopAll_Click(object sender, MouseButtonEventArgs e) { StopActive(); Notice("已停止语音、屏幕共享和远程控制。"); }
}
