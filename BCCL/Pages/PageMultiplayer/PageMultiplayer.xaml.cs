using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BCCL.Core.App;
using BCCL.Core.Link.MCTier;
using BCCL.Multiplayer;

namespace BCCL;

public partial class PageMultiplayer
{
    private readonly MCTierSession session = MCTierSession.Current;
    private CancellationTokenSource? connecting;
    private bool busy;
    private enum EntryMode { Home, Create, Join }
    private bool hosting;
    private bool refreshingPeers;
    private bool creatingEntry;
    private bool pageActive;
    private bool preparingRoom;
    private CancellationTokenSource? nameCheck;
    private Task nameCheckTask = Task.CompletedTask;
    private TaskCompletionSource<bool>? expediteNameCheck;
    private MCTierRoom? checkingRoom;
    private Uri? checkingEndpoint;
    private MCTierDirectoryException? occupiedName;
    private MCTierDirectoryLease? previewLease;
    private Task previewRelease = Task.CompletedTask;
    private string peersSignature = "";
    private string PreferencesPath => Path.Combine(Paths.Data, "Multiplayer", "preferences.json");
    private sealed record Preferences(string Nickname, string Node, bool VirtualAdapter,
        bool UseDirectory = true, string DirectoryEndpoint = MCTierDirectoryLease.DefaultEndpoint);

    public PageMultiplayer()
    {
        InitializeComponent();
        foreach (var input in new[] { TextRoom, TextNickname, TextNode, TextDirectory })
            input.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(RoomIdentityChanged));
        TextPassword.PasswordChanged += RoomIdentityChanged;
        TextNickname.Text = Environment.UserName.Length <= 32 ? Environment.UserName : Environment.UserName[..32];
        LoadPreferences();
        Loaded += (_, _) => { pageActive = true; session.Changed += SessionChanged; UpdateSession(); ScheduleNameCheck(); };
        Unloaded += (_, _) => { pageActive = false; session.Changed -= SessionChanged; ReleasePreview(); };
    }

    private void SessionChanged() => Dispatcher.BeginInvoke(new Action(UpdateSession));

    private void UpdateSession()
    {
        LabStatus.Text = session.Status;
        LabRoomTitle.Text = session.Running ? $"当前大厅：{session.Request!.Room.Name}" : hosting ? "正在创建大厅…" : "正在加入大厅…";
        LabAddress.Text = session.Running ? $"本机虚拟地址：{session.LocalAddress}\n连接节点：{session.Request!.Room.Node}" : "正在准备网络，请稍候。";
        LabSessionHelp.Text = hosting ? $"把邀请发给朋友，并告诉他们游戏端口：{session.Request?.GamePort.ToString() ?? TextHostPort.Text}。" : "选好房主和游戏端口后，即可生成 Minecraft 连接地址。";
        CardSession.Visibility = busy || session.Running ? Visibility.Visible : Visibility.Collapsed;
        CardGame.Visibility = session.Running ? Visibility.Visible : Visibility.Collapsed;
        PanForm.Visibility = session.Running ? Visibility.Collapsed : Visibility.Visible;
        PanForm.IsEnabled = !busy && !session.Running;
        BtnDisconnect.IsEnabled = !busy && session.Running;
        BtnInvite.IsEnabled = session.Running;
        BtnInvite.Visibility = session.Running ? Visibility.Visible : Visibility.Collapsed;
        BtnDisconnect.Visibility = session.Running ? Visibility.Visible : Visibility.Collapsed;
        BtnCancel.Visibility = busy && connecting is not null ? Visibility.Visible : Visibility.Collapsed;
        BtnForward.IsEnabled = !busy && session.Running;
        BtnCopyGame.IsEnabled = session.GameAddress.Length > 0;
        LabGameAddress.Text = session.GameAddress.Length > 0 ? $"3. 在 Minecraft 的“直接连接”中填写：{session.GameAddress}" : "3. 在 Minecraft → 多人游戏 → 直接连接中填写生成的地址。";
        var peers = session.Peers.Where(p => !p.IsLocal).ToList();
        LabMembers.Text = peers.Count == 0 ? "还没发现朋友，正在等待加入。" : $"发现 {peers.Count} 位朋友，成员状态会自动更新。";
        var signature = string.Join("\n", peers.Select(p => p.Display));
        if (signature == peersSignature) return;
        peersSignature = signature;
        var selectedIp = (ComboPeers.SelectedItem as MCTierPeer)?.Ip;
        refreshingPeers = true;
        try
        {
            ComboPeers.ItemsSource = peers;
            ComboPeers.SelectedItem = peers.FirstOrDefault(p => p.Ip == selectedIp);
            if (ComboPeers.SelectedItem is null && peers.Count == 1 && string.IsNullOrWhiteSpace(TextRemoteIp.Text))
            {
                ComboPeers.SelectedItem = peers[0];
                TextRemoteIp.Text = peers[0].Ip;
            }
        }
        finally { refreshingPeers = false; }
    }

    private void Notice(string message)
    {
        LabNotice.Text = message;
        PanNotice.Visibility = Visibility.Visible;
    }

    private void ChooseCreate_Click(object sender, MouseButtonEventArgs e) => ShowEntry(EntryMode.Create);
    private void ChooseJoin_Click(object sender, MouseButtonEventArgs e) => ShowEntry(EntryMode.Join);
    private void Back_Click(object sender, MouseButtonEventArgs e) => ShowEntry(EntryMode.Home);

    private void ShowEntry(EntryMode mode)
    {
        if (busy || session.Running) return;
        var create = mode == EntryMode.Create;
        creatingEntry = create;
        ReleasePreview();
        CardHome.Visibility = mode == EntryMode.Home ? Visibility.Visible : Visibility.Collapsed;
        CardEntry.Visibility = mode == EntryMode.Home ? Visibility.Collapsed : Visibility.Visible;
        PanJoin.Visibility = mode == EntryMode.Join ? Visibility.Visible : Visibility.Collapsed;
        PanHostPort.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        BtnCreate.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        BtnJoin.Visibility = mode == EntryMode.Join ? Visibility.Visible : Visibility.Collapsed;
        LabEntryTitle.Text = create ? "创建大厅" : "加入大厅";
        ExpRoom.Visibility = mode == EntryMode.Join ? Visibility.Visible : Visibility.Collapsed;
        ExpRoom.IsExpanded = false;
        TextRoom.HintText = create ? "填写房间名称，留空可自动生成" : "输入房主告诉你的房间名称";
        LabNameStatus.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        LabRoomHelp.Text = create ? "密码无格式要求，可以留空；留空时，知道房间名称的人即可加入。" : "房主设置了密码才需要填写，空格也会保留。房间名称、密码和连接节点需与房主一致。";
        LabEntryHelp.Text = create ? "创建时会申请管理员权限，授权后即可创建并邀请朋友。" : "输入房间名称即可加入，也可以展开上方的邀请链接入口。";
        PanNotice.Visibility = Visibility.Collapsed;
        PanBack.ScrollToHome();
        ScheduleNameCheck();
    }

    private void RoomIdentityChanged(object sender, RoutedEventArgs e)
    {
        if (preparingRoom || busy) return;
        if (ReferenceEquals(sender, TextRoom) && !creatingEntry && TextInvite is not null)
            TextInvite.Text = "";
        ScheduleNameCheck();
    }

    private void DirectorySettingChanged(object sender, RoutedEventArgs e) => ScheduleNameCheck();

    private void ReleasePreview()
    {
        nameCheck?.Cancel();
        nameCheck?.Dispose();
        nameCheck = null;
        if (previewLease is not null)
        {
            var release = previewLease.DisposeAsync().AsTask();
            previewRelease = previewRelease.IsCompleted ? release : Task.WhenAll(previewRelease, release);
        }
        previewLease = null;
    }

    private void ScheduleNameCheck()
    {
        if (TextRoom is null || LabNameStatus is null || busy || session.Running) return;
        var previousCheck = nameCheckTask;
        ReleasePreview();
        checkingRoom = null; checkingEndpoint = null; occupiedName = null;
        if (!creatingEntry) return;
        if (CheckDirectory?.IsChecked != true)
        { LabNameStatus.Text = "直接组网模式：不检测重名。"; return; }
        if (string.IsNullOrWhiteSpace(TextRoom.Text))
        { LabNameStatus.Text = "填写名称后自动检测重名；留空时创建前会自动生成并检测。"; return; }
        if (!pageActive) return;
        try
        {
            var room = new MCTierRoom(TextRoom.Text.Trim(), TextPassword.Password, TextNode.Text.Trim());
            room.Validate();
            var endpoint = MCTierDirectoryLease.ValidateEndpoint(TextDirectory.Text);
            nameCheck = new CancellationTokenSource();
            expediteNameCheck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            checkingRoom = room; checkingEndpoint = endpoint;
            LabNameStatus.Text = "正在检测房间名称…";
            nameCheckTask = CheckNameAsync(previousCheck, room, endpoint, TextNickname.Text.Trim(), expediteNameCheck.Task, nameCheck.Token);
        }
        catch (ArgumentException ex) { LabNameStatus.Text = ex.Message; }
    }

    private async Task CheckNameAsync(Task previousCheck, MCTierRoom room, Uri endpoint, string nickname, Task expedite, CancellationToken token)
    {
        MCTierDirectoryLease? candidate = null;
        try
        {
            await Task.WhenAny(Task.Delay(250, token), expedite).WaitAsync(token);
            try { await previousCheck.WaitAsync(token); }
            catch (Exception) when (!token.IsCancellationRequested) { /* A failed obsolete check must not poison the next name. */ }
            await previewRelease.WaitAsync(token);
            candidate = await MCTierDirectoryLease.ReserveAsync(room, nickname, endpoint, token);
            token.ThrowIfCancellationRequested();
            previewLease = candidate;
            var retained = candidate;
            candidate = null;
            retained.Closed += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(previewLease, retained)) LabNameStatus.Text = "名称保留已失效，创建时将重新检测。";
            }));
            LabNameStatus.Text = retained.IsAlive ? "名称可用，已临时保留。退出此页会释放名称。" : "名称保留已失效，创建时将重新检测。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or CryptographicException)
        {
            if (!token.IsCancellationRequested)
            {
                occupiedName = ex is MCTierDirectoryException { NameOccupied: true } duplicate ? duplicate : null;
                LabNameStatus.Text = ex is MCTierDirectoryException { NameOccupied: true }
                    ? "该房间名称已被使用，请换一个名称。" : ex.Message;
            }
        }
        finally { if (candidate is not null) await candidate.DisposeAsync(); }
    }

    private void ApplyInvite()
    {
        var room = MCTierRoom.ParseInvite(TextInvite.Text);
        var link = System.Text.RegularExpressions.Regex.Match(TextInvite.Text, @"mctier://join/?\?[^\s]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (link.Success)
        {
            var query = new Uri(link.Value).Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2)).ToDictionary(pair => System.Net.WebUtility.UrlDecode(pair[0]),
                    pair => pair.Length == 2 ? System.Net.WebUtility.UrlDecode(pair[1]) : "", StringComparer.OrdinalIgnoreCase);
            if (query.TryGetValue("signal", out var signal) && !string.IsNullOrWhiteSpace(signal))
                TextDirectory.Text = MCTierDirectoryLease.ValidateEndpoint(signal).AbsoluteUri;
            if (query.TryGetValue("bcclNetwork", out var network))
            {
                if (network is not ("managed" or "direct")) throw new ArgumentException("邀请中的组网方式无效。");
                CheckDirectory.IsChecked = network == "managed";
            }
        }
        TextRoom.Text = room.Name;
        TextPassword.Password = room.Password;
        TextNode.Text = room.Node;
    }

    private void Create_Click(object sender, MouseButtonEventArgs e) => Start(true);
    private void Join_Click(object sender, MouseButtonEventArgs e) => Start(false);

    private MCTierRoom PrepareRoom(bool create)
    {
        preparingRoom = true;
        try
        {
            if (!create && !string.IsNullOrWhiteSpace(TextInvite.Text)) ApplyInvite();
            if (!create && string.IsNullOrWhiteSpace(TextRoom.Text))
                throw new ArgumentException("请输入房间名称，或展开邀请入口粘贴朋友的邀请。");
            if (create && string.IsNullOrWhiteSpace(TextRoom.Text)) TextRoom.Text = "BCCL-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            var room = new MCTierRoom(TextRoom.Text.Trim(), TextPassword.Password, TextNode.Text.Trim());
            room.Validate();
            return room;
        }
        finally { preparingRoom = false; }
    }

    private async void Start(bool create)
    {
        if (busy || session.Running) return;
        try
        {
            var room = PrepareRoom(create);
            var port = Port(TextHostPort.Text);
            hosting = create;
            ExpGame.IsExpanded = !create;
            busy = true;
            connecting = new CancellationTokenSource();
            PanNotice.Visibility = Visibility.Collapsed;
            UpdateSession();
            if (CheckDirectory.IsChecked == true)
            {
                var endpoint = MCTierDirectoryLease.ValidateEndpoint(TextDirectory.Text);
                if (create && checkingRoom == room && checkingEndpoint == endpoint)
                {
                    var checkWasPending = !nameCheckTask.IsCompleted;
                    expediteNameCheck?.TrySetResult(true);
                    await nameCheckTask.WaitAsync(connecting.Token);
                    if (checkWasPending && occupiedName is not null) throw occupiedName;
                }
                else
                {
                    nameCheck?.Cancel();
                    await nameCheckTask.WaitAsync(connecting.Token);
                }
                await previewRelease.WaitAsync(connecting.Token);
                var reservation = previewLease;
                previewLease = null;
                if (reservation is not null && (!reservation.IsAlive || reservation.Room != room || reservation.Endpoint != endpoint))
                { await reservation.DisposeAsync(); reservation = null; }
                LabNameStatus.Text = "正在确认房间名称…";
                await session.StartRegisteredAsync(room, TextNickname.Text.Trim(), ComboMode.SelectedIndex == 1,
                    port, reservation, create, endpoint, connecting.Token);
            }
            else
            {
                ReleasePreview();
                if (create) await session.CreateAsync(room, TextNickname.Text.Trim(), ComboMode.SelectedIndex == 1, port, connecting.Token);
                else await session.StartAsync(room, TextNickname.Text.Trim(), ComboMode.SelectedIndex == 1, port, connecting.Token);
            }
            SavePreferences();
            Notice(create ? "邀请已准备好，可以复制给朋友；网络状态会在下方更新。" : "正在寻找同一大厅的朋友，请等待成员出现。");
            PanBack.ScrollToHome();
        }
        catch (OperationCanceledException) { Notice("已取消连接。"); }
        catch (MCTierDirectoryException ex)
        {
            LabNameStatus.Text = ex.NameOccupied ? "该房间名称已被使用，请换一个名称。" : "名称检测未通过，请重试。";
            Notice(ex.Message);
        }
        catch (Exception ex) { Notice(ex.Message); }
        finally
        {
            connecting?.Dispose();
            connecting = null;
            busy = false;
            UpdateSession();
        }
    }

    private void Cancel_Click(object sender, MouseButtonEventArgs e) => connecting?.Cancel();
    private async void Disconnect_Click(object sender, MouseButtonEventArgs e)
    {
        busy = true;
        UpdateSession();
        try { await session.StopAsync(); }
        catch (Exception ex) { Notice(ex.Message); }
        finally
        {
            busy = false;
            if (!session.Running) { TextRemoteIp.Text = ""; ShowEntry(EntryMode.Home); Notice("已退出大厅。"); }
            UpdateSession();
        }
    }

    private void Invite_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (session.Request is null) return;
            var invite = session.Request.Room.ToInvite() + "&bcclNetwork=" + (session.Request.ManagedRoom ? "managed" : "direct");
            if (session.Request.ManagedRoom) invite += "&signal=" + Uri.EscapeDataString(TextDirectory.Text.Trim());
            Clipboard.SetText(invite);
            Notice("邀请已复制。收到邀请的玩家可加入相同虚拟网络。");
        }
        catch (Exception ex) { Notice("无法复制邀请：" + ex.Message); }
    }

    private void Peer_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingPeers) return;
        if (ComboPeers.SelectedItem is MCTierPeer peer) TextRemoteIp.Text = peer.Ip;
    }

    private void Forward_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            session.Forward(TextRemoteIp.Text.Trim(), Port(TextRemotePort.Text));
            UpdateSession();
            Notice("本机游戏入口已准备。请在 Minecraft 中使用下方地址；游戏连接结果由 Minecraft 显示。");
        }
        catch (Exception ex) { Notice(ex.Message); }
    }

    private void CopyGame_Click(object sender, MouseButtonEventArgs e)
    {
        try { Clipboard.SetText(session.GameAddress); Notice("游戏地址已复制。"); }
        catch (Exception ex) { Notice("无法复制游戏地址：" + ex.Message); }
    }

    private static int Port(string text) => int.TryParse(text, out var port) && port is >= 1 and <= 65535 ? port : throw new ArgumentException("游戏端口应为 1–65535。");

    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath));
            if (settings is null) return;
            TextNickname.Text = settings.Nickname;
            TextNode.Text = settings.Node;
            ComboMode.SelectedIndex = settings.VirtualAdapter ? 1 : 0;
            CheckDirectory.IsChecked = settings.UseDirectory;
            TextDirectory.Text = settings.DirectoryEndpoint;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Notice("联机偏好未能读取，已使用默认设置。"); }
    }

    private void SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(new Preferences(TextNickname.Text.Trim(), TextNode.Text.Trim(), ComboMode.SelectedIndex == 1,
                CheckDirectory.IsChecked == true, TextDirectory.Text.Trim())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice("已连接，但联机偏好未能保存。"); }
    }
}
