using System.Globalization;
using System.IO;
using System.Net;
using System.Windows.Media.Imaging;
using BCCL.Core.App;
using BCCL.Core.IO.Net.Http;
using BCCL.Core.Link.McPing;
using BCCL.Core.Minecraft;
using BCCL.Core.UI;
using BCCL.Network;

namespace BCCL;

internal static class RecommendedServers
{
    internal const int Limit = 50;
    internal const int StatusParallelism = 8;
    private const int DefaultJavaPort = 25565;
    private const int StatusReuseMilliseconds = 20_000;
    private const int StatusKeepMilliseconds = 300_000;

    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    private static readonly TimeOnly RefreshTime = new(16, 0);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static readonly SemaphoreSlim StatusApiGate = new(1, 1);
    private static readonly SemaphoreSlim StatusWorkers = new(StatusParallelism, StatusParallelism);
    private static readonly Dictionary<string, StatusRequest> StatusRequests = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DirectoryCountsLock = new();
    private static readonly Lazy<Snapshot?> Saved = new(ReadCache);
    private static Snapshot? _latest;
    private static DateOnly? _attemptedSlot;
    private static long _retryAfter;
    private static int _started;
    private static long _nextStatusApiRequest;
    private static Task<Entry[]>? _directoryCounts;
    private static long _directoryCountsUntil;

    internal sealed record Entry(string Name, string Address, string Description,
        int? ReportedPlayers = null, DateTimeOffset? ReportedAt = null);
    internal sealed record Snapshot(DateOnly Slot, Entry[] Entries);
    internal sealed record LiveStatus(int? OnlinePlayers, BitmapSource? Logo)
    {
        internal bool IsDirectoryCount { get; init; }
        internal bool IsStale { get; init; }
        internal long? LatencyMs { get; init; }
        internal bool? IsOverseas { get; init; }
    }
    private sealed record StatusRequest(Task<LiveStatus?> Task)
    {
        internal long FinishedAt { get; set; }
        internal LiveStatus? Partial { get; set; }
    }

    private enum DirectorySource { CraftRank, Spawnlist, Msid }

    internal static event Action? Updated;
    internal static event Action<string>? StatusAvailable;
    internal static Snapshot? Current => Volatile.Read(ref _latest) ?? Saved.Value;

    internal static DateOnly GetSlot(DateTimeOffset time) =>
        DateOnly.FromDateTime(time.ToOffset(BeijingOffset).DateTime - RefreshTime.ToTimeSpan());

    internal static LiveStatus? GetInitialStatus(Entry entry)
    {
        LiveStatus? previous = null;
        var age = long.MaxValue;
        lock (StatusRequests)
        {
            if (StatusRequests.TryGetValue(entry.Address, out var request))
            {
                previous = request.Task.IsCompletedSuccessfully ? request.Task.Result : request.Partial;
                age = request.Task.IsCompleted ? Environment.TickCount64 - request.FinishedAt : 0;
                if (previous?.OnlinePlayers is not null && age < StatusReuseMilliseconds)
                    return previous;
            }
        }

        var count = IsRecentDirectoryCount(entry, DateTimeOffset.UtcNow) ? entry.ReportedPlayers : null;
        if (count is null && previous?.OnlinePlayers is not null && age < StatusKeepMilliseconds)
            return previous with { IsStale = true };
        return count is null && previous is null ? null :
            (previous ?? new LiveStatus(null, null)) with
            {
                OnlinePlayers = count,
                IsDirectoryCount = count is not null,
                IsStale = age >= StatusReuseMilliseconds,
                LatencyMs = age < StatusKeepMilliseconds ? previous?.LatencyMs : null
            };
    }

    internal static Task<LiveStatus?> QueryStatusAsync(string address, CancellationToken cancellationToken,
        bool needsLogo)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<LiveStatus?> task;
        lock (StatusRequests)
        {
            var now = Environment.TickCount64;
            if (StatusRequests.TryGetValue(address, out var request) &&
                (!request.Task.IsCompleted || now - request.FinishedAt < StatusReuseMilliseconds))
                return request.Task.WaitAsync(cancellationToken);

            var previous = request?.Task.IsCompletedSuccessfully == true ? request.Task.Result : null;
            var retained = previous is null ? null :
                now - request!.FinishedAt < StatusKeepMilliseconds ? previous with { IsStale = true } :
                new LiveStatus(null, previous.Logo) { IsOverseas = previous.IsOverseas };
            if (StatusRequests.Count >= Limit * 2)
            {
                foreach (var old in StatusRequests.Where(pair => pair.Value.Task.IsCompleted &&
                    now - pair.Value.FinishedAt >= StatusReuseMilliseconds).Select(pair => pair.Key).ToArray())
                    StatusRequests.Remove(old);
            }
            task = Task.Run(() => LoadStatusAsync(address, needsLogo, previous?.Logo));
            StatusRequests[address] = new StatusRequest(task) { Partial = retained };
        }
        return task.WaitAsync(cancellationToken);
    }

    private static async Task<LiveStatus?> LoadStatusAsync(string address, bool needsLogo, BitmapSource? previousLogo)
    {
        // A page may close while the startup warmup or another view still needs the result.
        await StatusWorkers.WaitAsync().ConfigureAwait(false);
        try
        {
            var status = await QueryStatusCoreAsync(address, CancellationToken.None, needsLogo && previousLogo is null)
                .ConfigureAwait(false);
            return status is null ? (previousLogo is null ? null : new LiveStatus(null, previousLogo)) :
                status with { Logo = status.Logo ?? previousLogo };
        }
        finally
        {
            lock (StatusRequests)
                StatusRequests[address].FinishedAt = Environment.TickCount64;
            StatusWorkers.Release();
        }
    }

    private static async Task<LiveStatus?> QueryStatusCoreAsync(string address, CancellationToken cancellationToken,
        bool needsLogo)
    {
        var direct = await QueryDirectStatusAsync(address, cancellationToken).ConfigureAwait(false);
        if (direct?.OnlinePlayers is not null && (!needsLogo || direct.Logo is not null))
            return direct;
        if (cancellationToken.IsCancellationRequested)
            return direct;
        PublishPartialStatus(address, direct);

        var fallback = await QueryPublicStatusAsync(address, cancellationToken).ConfigureAwait(false);
        var count = direct?.OnlinePlayers ?? fallback?.OnlinePlayers;
        var logo = direct?.Logo ?? fallback?.Logo;
        var result = (direct ?? new LiveStatus(null, null)) with { OnlinePlayers = count, Logo = logo };
        if (count is not null)
            return result;
        if (logo is not null && !ReferenceEquals(logo, direct?.Logo))
            PublishPartialStatus(address, result);

        var reported = await QueryDirectoryCountAsync(address, cancellationToken).ConfigureAwait(false);
        return reported is null && logo is null && direct is null ? null :
            result with { OnlinePlayers = reported, IsDirectoryCount = reported is not null };
    }

    private static void PublishPartialStatus(string address, LiveStatus? status)
    {
        if (status is null)
            return;
        lock (StatusRequests)
        {
            if (!StatusRequests.TryGetValue(address, out var request))
                return;
            request.Partial = status with { Logo = status.Logo ?? request.Partial?.Logo };
        }
        StatusAvailable?.Invoke(address);
    }

    private static async Task<int?> QueryDirectoryCountAsync(string address, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || NormalizeAddress(address, null) is not { } host)
            return null;

        var now = DateTimeOffset.UtcNow;
        bool IsRecentEntry(Entry entry) => string.Equals(host, entry.Address, StringComparison.OrdinalIgnoreCase) &&
            IsRecentDirectoryCount(entry, now);
        if (Current?.Entries.FirstOrDefault(IsRecentEntry) is { } current)
            return current.ReportedPlayers;

        Task<Entry[]> refresh;
        lock (DirectoryCountsLock)
        {
            var tick = Environment.TickCount64;
            if (_directoryCounts is null || tick >= _directoryCountsUntil)
            {
                _directoryCounts = FetchAsync(DirectorySource.CraftRank);
                _directoryCountsUntil = tick + 60_000;
            }
            refresh = _directoryCounts;
        }
        try
        {
            return (await refresh.WaitAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(IsRecentEntry)?.ReportedPlayers;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
    }

    private static bool IsRecentDirectoryCount(Entry entry, DateTimeOffset now) =>
        entry.ReportedPlayers is >= 0 && entry.ReportedAt is { } at &&
        at >= now.AddMinutes(-5) && at <= now.AddMinutes(1);

    private static async Task<LiveStatus?> QueryDirectStatusAsync(string address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        bool? overseas = null;
        try
        {
            var target = await ServerAddressResolver.GetResolvedServerAddressAsync(address, timeout.Token, publicOnly: true)
                .ConfigureAwait(false);
            if (!IPAddress.TryParse(target.Ip, out var ip))
                return null;

            using var query = new McPingService(target.Host, new IPEndPoint(ip, target.Port));
            var request = query.QueryStatusAsync(timeout.Token);
            overseas = ServerIpRegion.IsOverseas(ip);
            var (response, latency) = await request.ConfigureAwait(false);
            if (response is not JsonObject)
                latency = null;
            var status = ReadLiveStatus(response, "favicon");
            if (status is not null || latency is not null || overseas is not null)
                return (status ?? new LiveStatus(null, null)) with { LatencyMs = latency, IsOverseas = overseas };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Server] 查询推荐服务器状态失败：{address}", ModBase.LogLevel.Debug);
        }
        return overseas is null ? null : new LiveStatus(null, null) { IsOverseas = overseas };
    }

    private static async Task<LiveStatus?> QueryPublicStatusAsync(string address, CancellationToken cancellationToken)
    {
        if (NormalizeAddress(address, null) is not { } host)
            return null;
        var uri = new Uri("minecraft://" + host);
        if (IPAddress.TryParse(uri.IdnHost, out var literal) && !ServerAddressResolver.IsPublicAddress(literal))
            return null;

        try
        {
            Task<JsonNode> request;
            await StatusApiGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var remaining = _nextStatusApiRequest - Environment.TickCount64;
                if (remaining > 0)
                    await Task.Delay((int)remaining, cancellationToken).ConfigureAwait(false);
                // The provider allows five requests per second per IP.
                _nextStatusApiRequest = Environment.TickCount64 + 260;
                request = Requester.FetchJsonAsync("https://api.mcstatus.io/v2/status/java/" + Uri.EscapeDataString(host),
                    new RequestParam { Timeout = 5000, Retries = 1, CancellationToken = cancellationToken });
            }
            finally { StatusApiGate.Release(); }

            var response = await request.ConfigureAwait(false) as JsonObject;
            if (response?["online"] is not JsonValue online || !online.TryGetValue<bool>(out var available) || !available ||
                response["expires_at"] is not JsonValue expiry || !expiry.TryGetValue<long>(out var expires) ||
                expires < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return null;

            return ReadLiveStatus(response, "icon");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Server] 备用状态查询失败：{host}", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private static LiveStatus? ReadLiveStatus(JsonNode? response, string iconKey)
    {
        if (response is not JsonObject status)
            return null;
        var count = status["players"] is JsonObject players && players["online"] is JsonValue online &&
                    online.TryGetValue<int>(out var reported) && reported >= 0 ? (int?)reported : null;
        var logo = status[iconKey] is JsonValue icon && icon.TryGetValue<string>(out var data)
            ? ImageLoaderHelper.DecodeJavaServerLogo(data)
            : null;
        return count is null && logo is null ? null : new LiveStatus(count, logo);
    }

    internal static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _ = Task.Run(RunScheduleAsync);
    }

    private static Task PreloadStatusesAsync(Snapshot snapshot) =>
        Parallel.ForEachAsync(snapshot.Entries, new ParallelOptions { MaxDegreeOfParallelism = StatusParallelism },
            async (entry, token) => await QueryStatusAsync(entry.Address, token, needsLogo: true).ConfigureAwait(false));

    private static async Task RunScheduleAsync()
    {
        var warmed = Current;
        if (warmed is not null)
            _ = PreloadStatusesAsync(warmed);

        while (true)
        {
            var before = GetSlot(DateTimeOffset.UtcNow);
            var snapshot = await RefreshAsync().ConfigureAwait(false);
            if (snapshot is not null && !ReferenceEquals(snapshot, warmed))
            {
                warmed = snapshot;
                _ = PreloadStatusesAsync(snapshot);
            }

            // Wake at the boundary and periodically recheck after time changes or sleep.
            var now = DateTimeOffset.UtcNow;
            var slot = GetSlot(now);
            if (slot != before)
                continue;
            var next = new DateTimeOffset(slot.AddDays(1).ToDateTime(RefreshTime), BeijingOffset);
            var wait = next - now;
            await Task.Delay(wait < TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
    }

    internal static async Task<Snapshot?> RefreshAsync(bool retry = false)
    {
        await RefreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var slot = GetSlot(DateTimeOffset.UtcNow);
            var current = Current;
            if (current is { Entries.Length: Limit } && current.Slot == slot)
                return current;

            if (!retry && _attemptedSlot == slot && Environment.TickCount64 < _retryAfter)
                return current;

            _attemptedSlot = slot;
            _retryAfter = Environment.TickCount64 + (long)RetryInterval.TotalMilliseconds;

            var primary = await Task.WhenAll(FetchAsync(DirectorySource.CraftRank), FetchAsync(DirectorySource.Spawnlist))
                .ConfigureAwait(false);
            var entries = primary.SelectMany(results => results).DistinctBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase)
                .Take(Limit).ToArray();

            if (entries.Length < Limit)
            {
                var extra = await FetchAsync(DirectorySource.Msid).ConfigureAwait(false);
                entries = entries.Concat(extra).DistinctBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase)
                    .Take(Limit).ToArray();
            }

            if (entries.Length == 0 || (entries.Length < Limit && current is not null && current.Entries.Length > entries.Length))
                return current;

            var refreshed = new Snapshot(slot, entries);
            Volatile.Write(ref _latest, refreshed);
            SaveCache(refreshed);
            Updated?.Invoke();
            return refreshed;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private static async Task<Entry[]> FetchAsync(DirectorySource source)
    {
        var address = source switch
        {
            DirectorySource.CraftRank => "https://craftrank.org/api/v1/top?edition=java&limit=50",
            DirectorySource.Spawnlist => "https://api.spawnlist.gg/v1/servers?game=minecraft_java&sort=players&limit=100",
            _ => "https://api.minecraftserver.id/api/v1/public/servers?limit=50&sort=players&edition=JAVA&status=ONLINE"
        };

        try
        {
            var json = await Requester.FetchJsonAsync(address, new RequestParam { Timeout = 8000, Retries = 1 })
                .ConfigureAwait(false);
            var listings = source switch
            {
                DirectorySource.CraftRank => json["servers"],
                DirectorySource.Spawnlist => json,
                _ => json["data"]?["servers"]
            };

            if (listings is not JsonArray servers)
                throw new InvalidDataException("The directory did not return a server list.");

            return servers.OfType<JsonObject>().Select(server => Parse(server, source)).OfType<Entry>().ToArray();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Server] 服务器推荐数据源请求失败：{source}", ModBase.LogLevel.Debug);
            return [];
        }
    }

    private static Entry? Parse(JsonObject server, DirectorySource source)
    {
        var (isOnlineJava, host, description) = source switch
        {
            DirectorySource.CraftRank => (Text("edition") == "java" && Value<bool>("online") == true, Text("address"), Text("motd")),
            DirectorySource.Spawnlist => (Text("game") == "minecraft_java" && Value<int>("players") > 0, Text("ip"), Text("description")),
            _ => (Text("edition") == "JAVA" && Text("status") == "ONLINE", Text("address"), Text("description"))
        };
        var name = Text("name");
        var address = NormalizeAddress(host, Value<int>("port"));
        if (!isOnlineJava || name.Length == 0 || address is null)
            return null;

        if (source == DirectorySource.CraftRank && server["players"] is JsonObject players &&
            players["online"] is JsonValue online && online.TryGetValue<int>(out var count) && count >= 0 &&
            DateTimeOffset.TryParse(Text("last_checked"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkedAt))
            return new Entry(name, address, description.Length > 0 ? description : Text("version"), count, checkedAt);
        return new Entry(name, address, description.Length > 0 ? description : Text("version"));

        string Text(string key) => server[key] is JsonValue field && field.TryGetValue<string>(out var text) && text is not null
            ? string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            : "";

        T? Value<T>(string key) where T : struct =>
            server[key] is JsonValue field && field.TryGetValue<T>(out var value) ? value : null;
    }

    private static string? NormalizeAddress(string address, int? advertisedPort)
    {
        if (!Uri.TryCreate("minecraft://" + address, UriKind.Absolute, out var uri) ||
            uri.HostNameType == UriHostNameType.Unknown || uri.IsLoopback ||
            uri.UserInfo.Length != 0 || uri.PathAndQuery is not ("" or "/") || uri.Fragment.Length != 0)
            return null;

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.Dns && !host.Contains('.'))
            return null;
        if (uri.HostNameType == UriHostNameType.IPv6)
            host = $"[{host.Trim('[', ']')}]";

        var port = uri.Port == -1 ? advertisedPort ?? DefaultJavaPort : uri.Port;
        return port is > 0 and <= 65535 ? port == DefaultJavaPort ? host : $"{host}:{port}" : null;
    }

    private static string CachePath => Path.Combine(Paths.Data, "Cache", "RecommendedServers.json");

    private static Snapshot? ReadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            using var file = File.OpenRead(CachePath);
            var snapshot = JsonSerializer.Deserialize<Snapshot>(file);
            return snapshot is { Entries.Length: > 0 and <= Limit } && snapshot.Slot <= GetSlot(DateTimeOffset.UtcNow)
                ? snapshot
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ModBase.Log(ex, "[Server] 读取服务器推荐缓存失败", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private static void SaveCache(Snapshot snapshot)
    {
        var path = CachePath;
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                using (var file = File.Create(temporary))
                    JsonSerializer.Serialize(file, snapshot);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ModBase.Log(ex, "[Server] 保存服务器推荐缓存失败", ModBase.LogLevel.Debug);
        }
    }
}
