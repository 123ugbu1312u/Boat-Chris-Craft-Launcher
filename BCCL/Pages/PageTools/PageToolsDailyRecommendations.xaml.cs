using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BCCL.Core.App.Localization;
using static BCCL.ModComp;

namespace BCCL;

public partial class PageToolsDailyRecommendations
{
    private sealed record RecommendationItem(CompProject Project)
    {
        public string Name => Project.TranslatedName;
        public string Description => Project.Description?.ReplaceLineEndings(" ") ?? "";
    }

    private sealed class ServerItem(RecommendedServers.Entry server) : INotifyPropertyChanged
    {
        private long _lastCountAt;
        public RecommendedServers.Entry Server { get; } = server;
        public string Name => Server.Name;
        public string Description => string.IsNullOrEmpty(Server.Description)
            ? Server.Address
            : $"{Server.Address} · {Server.Description}";
        private int? OnlinePlayers { get; set; }
        public string PlayerCountText => OnlinePlayers is { } count
            ? (PlayerCountIsStale ? "≈" : "") + count.ToString("N0", CultureInfo.CurrentCulture)
            : "—";
        public bool PlayerCountIsStale { get; private set; }
        public bool PlayerCountIsFromDirectory { get; private set; }
        public string LatencyText { get; private set; } = "— ms";
        public bool IsOverseas { get; private set; }
        public BitmapSource? Logo { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public void UpdateStatus(RecommendedServers.LiveStatus? status)
        {
            if (status?.OnlinePlayers is { } count)
            {
                OnlinePlayers = count;
                _lastCountAt = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - _lastCountAt > 300_000)
                OnlinePlayers = null;
            PlayerCountIsFromDirectory = status?.IsDirectoryCount == true;
            PlayerCountIsStale = OnlinePlayers is not null &&
                (status?.OnlinePlayers is null || status.IsStale || PlayerCountIsFromDirectory);
            LatencyText = status?.LatencyMs is { } latency
                ? (status.IsStale ? "≈" : "") + latency.ToString(CultureInfo.CurrentCulture) + " ms"
                : "— ms";
            if (status?.IsOverseas is { } overseas)
                IsOverseas = overseas;
            Logo = status?.Logo ?? Logo;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DataTemplate _rowTemplate;
    private readonly DataTemplate _serverTemplate;
    private CompType? _category;
    private DateOnly? _displayDate;
    private DailyResourceRecommendations.Ranking? _displayedRanking;
    private RecommendedServers.Snapshot? _displayedServers;
    private ServerItem[] _serverItems = [];
    private CancellationTokenSource? _serverStatusRefresh;
    private bool _serversOpen;
    private bool _updatingServers;

    public PageToolsDailyRecommendations()
    {
        InitializeComponent();
        _rowTemplate = (DataTemplate)FindResource("RecommendationRow");
        _serverTemplate = (DataTemplate)FindResource("ServerRow");
        InitializeMotion();
        Loaded += async (_, _) =>
        {
            Resources["RecommendationMotionEnabled"] = MotionEnabled;
            RecommendedServers.Updated += ServerFeedUpdated;
            RecommendedServers.StatusAvailable += ServerStatusAvailable;
            _timer.Start();
            RefreshServerStatus();
            await RefreshActivePageAsync();
        };
        Unloaded += (_, _) =>
        {
            RecommendedServers.Updated -= ServerFeedUpdated;
            RecommendedServers.StatusAvailable -= ServerStatusAvailable;
            _timer.Stop();
            StopServerStatusRefresh();
            SetSkeleton(false);
            ResetCopyFeedback();
        };
        _timer.Tick += async (_, _) =>
        {
            RefreshServerStatus();
            await RefreshActivePageAsync();
        };
    }

    private async void Category_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { CommandParameter: string name } || !Enum.TryParse<CompType>(name, out var category))
            return;

        _category = category;
        OpenList();
        await RefreshAsync();
    }

    private void Categories_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && sender is UniformGrid categories)
            categories.Columns = e.NewSize.Width < 510 ? 2 : 3;
    }

    private async void ServerRecommendations_Click(object sender, RoutedEventArgs e)
    {
        _category = null;
        _serversOpen = true;
        OpenList();
        RankingTitle.SetResourceReference(TextBlock.TextProperty, "Tools.Daily.Server.Title");
        RankingScroll.ScrollToHome();
        await RefreshServersAsync();
    }

    private Task RefreshActivePageAsync(bool retry = false) =>
        _serversOpen ? RefreshServersAsync(retry) : RefreshAsync(force: retry);

    private async Task RefreshServersAsync(bool retry = false)
    {
        if (!IsLoaded || !_serversOpen || _updatingServers)
            return;

        _updatingServers = true;
        try
        {
            if (RecommendedServers.Current is { } cached)
                ShowServers(cached, animate: false);
            else
                SetStatus("Common.State.Loading");

            var snapshot = await RecommendedServers.RefreshAsync(retry);
            if (IsLoaded && _serversOpen)
                ShowServers(snapshot);
        }
        finally
        {
            _updatingServers = false;
        }
    }

    private void ServerFeedUpdated()
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && _serversOpen)
                ShowServers(RecommendedServers.Current);
        });
    }

    private void ServerStatusAvailable(string address)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsLoaded && _serversOpen &&
                _serverItems.FirstOrDefault(item => item.Server.Address == address) is { } row)
                row.UpdateStatus(RecommendedServers.GetInitialStatus(row.Server));
        });
    }

    private void ShowServers(RecommendedServers.Snapshot? snapshot, bool animate = true)
    {
        if (snapshot is null)
        {
            SetStatus("Tools.Daily.Server.Unavailable", retry: true);
            return;
        }

        if (!ReferenceEquals(snapshot, _displayedServers))
        {
            StopServerStatusRefresh();
            _displayedServers = snapshot;
            _serverItems = snapshot.Entries.Select(server => new ServerItem(server)).ToArray();
            foreach (var item in _serverItems)
                item.UpdateStatus(RecommendedServers.GetInitialStatus(item.Server));
            PresentRows(_serverItems.Select(item => (item.Server.Address, (object)item)),
                _serverTemplate, lazy: false, animate);
            RefreshServerStatus();
        }

        if (snapshot.Slot != RecommendedServers.GetSlot(DateTimeOffset.UtcNow))
            SetStatus("Tools.Daily.Server.Cached", true, snapshot.Slot.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        else if (snapshot.Entries.Length < RecommendedServers.Limit)
            SetStatus("Tools.Daily.Server.Incomplete", true, snapshot.Entries.Length, RecommendedServers.Limit);
        else
            StatusPanel.Visibility = Visibility.Collapsed;
    }

    private void RefreshServerStatus()
    {
        if (!IsLoaded || !_serversOpen || _serverItems.Length == 0 || _serverStatusRefresh is not null)
            return;

        var refresh = new CancellationTokenSource();
        _serverStatusRefresh = refresh;
        _ = RefreshServerStatusAsync(_serverItems, refresh);
    }

    private async Task RefreshServerStatusAsync(ServerItem[] items, CancellationTokenSource refresh)
    {
        try
        {
            await Parallel.ForEachAsync(items, new ParallelOptions
            {
                MaxDegreeOfParallelism = RecommendedServers.StatusParallelism,
                CancellationToken = refresh.Token
            }, async (item, token) =>
            {
                var status = await RecommendedServers.QueryStatusAsync(item.Server.Address, token, needsLogo: item.Logo is null);
                await Dispatcher.InvokeAsync(() => item.UpdateStatus(status), DispatcherPriority.Background, token);
            });
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
        finally
        {
            if (ReferenceEquals(_serverStatusRefresh, refresh))
                _serverStatusRefresh = null;
            refresh.Dispose();
        }
    }

    private void StopServerStatusRefresh()
    {
        var refresh = _serverStatusRefresh;
        _serverStatusRefresh = null;
        refresh?.Cancel();
    }

    private void OpenList() => SwitchPane(showList: true);

    private void BackToCategories_Click(object sender, RoutedEventArgs e)
    {
        _category = null;
        _serversOpen = false;
        _displayedServers = null;
        _serverItems = [];
        StopServerStatusRefresh();
        _displayDate = null;
        _displayedRanking = null;
        ClearRows();
        SetSkeleton(false);
        SwitchPane(showList: false);
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await RefreshActivePageAsync(retry: true);

    private async Task RefreshAsync(bool force = false)
    {
        if (!IsLoaded || _category is not { } category)
            return;

        var date = DateOnly.FromDateTime(DateTime.Now);
        RankingTitle.Text = Lang.Text("Tools.Daily.RankingTitle",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), GetCompTypeName(category));
        if (_displayDate != date)
        {
            _displayDate = date;
            _displayedRanking = null;
            ClearRows();
            RankingScroll.ScrollToHome();
        }

        try
        {
            var load = DailyResourceRecommendations.GetOrStart(category, date, force);
            var alreadyLoaded = load.Completion.IsCompleted;
            if (!alreadyLoaded && _displayedRanking is null)
            {
                if (load.Preview.IsCompletedSuccessfully)
                    ShowRanking(load.Preview.Result, animate: false);
                else
                {
                    SetStatus("Common.State.Loading");
                    var ready = await Task.WhenAny(load.Preview, load.Completion);
                    if (ready == load.Preview && IsCurrent(category, date) &&
                        DailyResourceRecommendations.IsLatest(category, load))
                        ShowRanking(load.Preview.Result);
                }
            }

            var ranking = await load.Completion;
            if (IsCurrent(category, date) && DailyResourceRecommendations.IsLatest(category, load))
                ShowRanking(ranking, animate: !alreadyLoaded);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Comp] 每日推荐加载失败", ModBase.LogLevel.Debug);
            if (IsCurrent(category, date))
                SetStatus("Tools.Daily.Unavailable", retry: true);
        }
    }

    private bool IsCurrent(CompType category, DateOnly date) =>
        IsLoaded && _category == category && date == DateOnly.FromDateTime(DateTime.Now);

    private void ShowRanking(DailyResourceRecommendations.Ranking ranking, bool animate = true)
    {
        if (ReferenceEquals(_displayedRanking, ranking))
            return;

        if (ranking.IsPreview && _displayedRanking is not null)
            return;

        if (ranking.Projects.Count == 0)
        {
            SetStatus(ranking.SourceFailed ? "Tools.Daily.Unavailable" : "Download.Comp.List.NoResultsSimple",
                retry: ranking.SourceFailed);
            return;
        }

        // Keep the last usable same-day list if a refresh loses a source or its daily ranking.
        if (_displayedRanking is { UsesDailyIncrease: true, IsPreview: false, IsCached: false } && !ranking.UsesDailyIncrease)
        {
            if (ranking.SourceFailed)
                SetStatus("Tools.Daily.Partial", retry: true);
            return;
        }

        _displayedRanking = ranking;
        PresentRows(ranking.Projects.Select(project =>
                ($"{project.FromCurseForge}:{project.Id}", (object)new RecommendationItem(project))),
            _rowTemplate, lazy: true, animate: animate && !ranking.IsCached);
        var status = (ranking.IsCached, ranking.IsPreview, ranking.SourceFailed, ranking.UsesDailyIncrease) switch
        {
            (true, true, _, _) => "Tools.Daily.CachedUpdating",
            (true, false, _, _) => "Tools.Daily.CachedOffline",
            (_, true, _, _) => "Tools.Daily.Updating",
            (_, _, true, true) => "Tools.Daily.Partial",
            (_, _, _, true) => "Tools.Daily.Estimate",
            _ => "Tools.Daily.Cumulative"
        };
        SetStatus(status, retry: ranking.SourceFailed);
    }

    private void SetStatus(string key, bool retry = false, params object[] arguments)
    {
        RankingStatus.Text = arguments.Length == 0 ? Lang.Text(key) : Lang.Text(key, arguments);
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        SetSkeleton(key == "Common.State.Loading");
    }

    private void ProjectLogo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MyImage { DataContext: RecommendationItem item } logo)
            item.Project.ApplyLogoToMyImage(logo);
    }

    private void CopyServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ServerItem item, Tag: TextBlock label })
            ModBase.ClipboardSet(item.Server.Address, showSuccessHint: false, () => ShowCopyFeedback(label));
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RecommendationItem item } || _category is not { } category)
            return;

        ModMain.frmMain.PageChange(new FormMain.PageStackData
        {
            page = FormMain.PageType.CompDetail,
            additional = (item.Project, [], null!, CompLoaderType.Any, category, null!)
        });
    }
}
