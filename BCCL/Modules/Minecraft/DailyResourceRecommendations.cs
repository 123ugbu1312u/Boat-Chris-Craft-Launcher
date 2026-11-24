using System.IO;
using BCCL.Network;
using static BCCL.ModComp;

namespace BCCL;

internal static class DailyResourceRecommendations
{
    private const int RankingSize = 100;
    private const int CurseForgePageSize = 50;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
    private static readonly object LoadLock = new();
    private static readonly Dictionary<CompType, LoadSession> Loads = new();
    private static readonly CompType[] WarmupOrder =
        [CompType.Mod, CompType.ModPack, CompType.DataPack, CompType.ResourcePack, CompType.Shader, CompType.World];
    private static int _warmupStarted;

    internal sealed record Ranking(
        IReadOnlyList<CompProject> Projects,
        bool UsesDailyIncrease,
        bool SourceFailed = false,
        bool IsPreview = false,
        bool IsCached = false);

    internal sealed class LoadSession : IProgress<Ranking>
    {
        private readonly TaskCompletionSource<Ranking> _preview = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateOnly Date { get; }
        public long StartedAt { get; } = Environment.TickCount64;
        public Task<Ranking> Preview => _preview.Task;
        public Task<Ranking> Completion { get; }

        internal LoadSession(CompType type, DateOnly date, LoadSession? previous)
        {
            Date = date;
            Completion = Task.Run(async () =>
            {
                if (previous is not null)
                {
                    try { await previous.Completion.ConfigureAwait(false); }
                    catch (Exception ex) { ModBase.Log(ex, "[Comp] 上一轮每日推荐加载失败", ModBase.LogLevel.Debug); }
                }

                return await LoadAsync(type, date, this).ConfigureAwait(false);
            });
        }

        void IProgress<Ranking>.Report(Ranking ranking) => _preview.TrySetResult(ranking);
    }

    private sealed record Baseline(DateOnly Date, Dictionary<string, int> Counts);
    private sealed record CachedRanking(DateOnly Date, bool UsesDailyIncrease, JsonObject[] Projects);
    private sealed record Candidate(CompProject Project, int? Increase);
    private sealed record SourcePage(CompSourceType Source, IReadOnlyList<CompProject> Projects, bool Failed = false);

    internal static void StartWarmup()
    {
        if (Interlocked.Exchange(ref _warmupStarted, 1) != 0)
            return;

        _ = Task.Run(() => Parallel.ForEachAsync(WarmupOrder, new ParallelOptions { MaxDegreeOfParallelism = 2 },
            async (type, _) =>
            {
                try
                {
                    var date = DateOnly.FromDateTime(DateTime.Now);
                    await GetOrStart(type, date).Completion.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, $"[Comp] 每日推荐后台预加载失败：{type}", ModBase.LogLevel.Debug);
                }
            }));
    }

    internal static LoadSession GetOrStart(CompType type, DateOnly date, bool force = false)
    {
        lock (LoadLock)
        {
            if (Loads.TryGetValue(type, out var current) && current.Date == date)
            {
                if (!current.Completion.IsCompleted)
                    return current;

                var interval = current.Completion.IsCompletedSuccessfully && !current.Completion.Result.SourceFailed
                    ? RefreshInterval
                    : RetryInterval;
                if (!force && Environment.TickCount64 - current.StartedAt < interval.TotalMilliseconds)
                    return current;
            }

            var next = new LoadSession(type, date, current is { Completion.IsCompleted: false } ? current : null);
            Loads[type] = next;
            return next;
        }
    }

    internal static bool IsLatest(CompType type, LoadSession load)
    {
        lock (LoadLock)
            return Loads.TryGetValue(type, out var current) && ReferenceEquals(current, load);
    }

    internal static async Task<Ranking> LoadAsync(CompType type, DateOnly date, IProgress<Ranking> progress)
    {
        var directory = Path.Combine(ModBase.pathTemp, "Cache", "DailyRecommendations");
        var rankingPath = Path.Combine(directory, $"{type}-ranking.json");
        var storage = new CompProjectStorage();
        var request = new CompProjectRequest(type, storage, RankingSize) { sort = CompSortType.Downloads };

        // Both offsets are known in advance. A slow page or provider must not hold back the first result.
        var firstCurseForgeUrl = request.GetCurseForgeAddress(CurseForgePageSize);
        storage.curseForgeOffset = CurseForgePageSize;
        var secondCurseForgeUrl = request.GetCurseForgeAddress(CurseForgePageSize);
        var pending = new List<Task<SourcePage>>
        {
            FetchPageAsync(type, CompSourceType.CurseForge, firstCurseForgeUrl),
            FetchPageAsync(type, CompSourceType.CurseForge, secondCurseForgeUrl)
        };
        if (type != CompType.World)
            pending.Add(FetchPageAsync(type, CompSourceType.Modrinth, request.GetModrinthAddress(RankingSize)));

        var cached = ReadRanking(rankingPath, date);
        if (cached is not null)
            progress.Report(cached with { IsPreview = true });

        var baselines = new Dictionary<CompSourceType, Baseline>();
        var changedBaselines = new HashSet<CompSourceType>();
        var candidates = new Dictionary<(CompSourceType Source, string Id), Candidate>();
        var shownPreview = cached is not null;
        var sourceFailed = false;
        while (pending.Count != 0)
        {
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            var page = await completed.ConfigureAwait(false);
            sourceFailed |= page.Failed;
            if (date != DateOnly.FromDateTime(DateTime.Now))
                return new Ranking([], UsesDailyIncrease: false);

            if (page.Projects.Count == 0)
                continue;
            if (!baselines.TryGetValue(page.Source, out var baseline))
            {
                var saved = ReadCache<Baseline>(BaselinePath(page.Source));
                baseline = saved is { Counts: not null } && saved.Date == date
                    ? saved
                    : new Baseline(date, new Dictionary<string, int>(StringComparer.Ordinal));
                baselines.Add(page.Source, baseline);
            }

            foreach (var project in page.Projects)
            {
                var key = (page.Source, project.Id);
                if (candidates.ContainsKey(key))
                    continue;

                int? increase = null;
                if (baseline.Counts.TryGetValue(project.Id, out var beginning))
                {
                    if (project.DownloadCount >= beginning)
                        increase = project.DownloadCount - beginning;
                }
                else
                {
                    baseline.Counts.Add(project.Id, project.DownloadCount);
                    changedBaselines.Add(page.Source);
                }

                candidates.Add(key, new Candidate(project, increase));
            }

            if (!shownPreview && pending.Count != 0 && candidates.Count != 0)
            {
                progress.Report(BuildRanking(candidates.Values) with { IsPreview = true });
                shownPreview = true;
            }
        }

        var ranking = BuildRanking(candidates.Values) with { SourceFailed = sourceFailed };
        foreach (var source in changedBaselines)
            SaveCache(BaselinePath(source), baselines[source]);

        if (sourceFailed && cached is not null &&
            (ranking.Projects.Count < cached.Projects.Count || (cached.UsesDailyIncrease && !ranking.UsesDailyIncrease)))
            return cached with { SourceFailed = true };

        if (ranking.Projects.Count != 0 && (!sourceFailed || cached is null))
            SaveCache(rankingPath, new CachedRanking(date, ranking.UsesDailyIncrease,
                ranking.Projects.Select(project => project.ToJson()).ToArray()));
        return ranking;

        string BaselinePath(CompSourceType source) => Path.Combine(directory, $"{type}-{source}.json");
    }

    private static Ranking BuildRanking(IReadOnlyCollection<Candidate> candidates)
    {
        var usesDailyIncrease = candidates.Any(candidate => candidate.Increase > 0);
        var ordered = usesDailyIncrease
            ? candidates.Where(candidate => candidate.Increase.HasValue)
                .OrderByDescending(candidate => candidate.Increase)
                .ThenByDescending(candidate => candidate.Project.DownloadCount)
            : candidates.OrderByDescending(candidate => candidate.Project.DownloadCount);

        var projects = new List<CompProject>(RankingSize);
        foreach (var candidate in ordered)
        {
            var project = candidate.Project;
            if (projects.Any(other => other.FromCurseForge != project.FromCurseForge && other.IsLike(project)))
                continue;

            projects.Add(project);
            if (projects.Count == RankingSize)
                break;
        }

        return new Ranking(projects, usesDailyIncrease);
    }

    private static async Task<SourcePage> FetchPageAsync(CompType type, CompSourceType source, string address)
    {
        try
        {
            var response = await ResourceListRequest.GetAsync(address).ConfigureAwait(false);
            var entries = response[source == CompSourceType.Modrinth ? "hits" : "data"]!.AsArray();
            var projects = new List<CompProject>(entries.Count);
            foreach (var entry in entries.OfType<JsonObject>())
            {
                try
                {
                    projects.Add(new CompProject(entry));
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, $"[Comp] 每日推荐资源解析失败：{type} / {source}", ModBase.LogLevel.Debug);
                }
            }

            return new SourcePage(source, projects, Failed: entries.Count != 0 && projects.Count == 0);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Comp] 每日推荐获取失败：{type} / {source}", ModBase.LogLevel.Debug);
            return new SourcePage(source, [], Failed: true);
        }
    }

    private static Ranking? ReadRanking(string path, DateOnly date)
    {
        var cached = ReadCache<CachedRanking>(path);
        if (cached is not { Projects.Length: > 0 } || cached.Date > date || date.DayNumber - cached.Date.DayNumber > 7)
            return null;

        try
        {
            return new Ranking(cached.Projects.Take(RankingSize).Select(project => new CompProject(project)).ToArray(),
                cached.Date == date && cached.UsesDailyIncrease, IsCached: true);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Comp] 每日推荐缓存无法解析", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private static T? ReadCache<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var file = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(file);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ModBase.Log(ex, "[Comp] 每日推荐缓存读取失败", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private static void SaveCache<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                using (var file = File.Create(temporary))
                    JsonSerializer.Serialize(file, value);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ModBase.Log(ex, "[Comp] 每日推荐缓存保存失败", ModBase.LogLevel.Debug);
        }
    }
}
