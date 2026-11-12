using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using BCCL.Core.IO.Net.Http;
using BCCL.Core.Logging;

namespace BCCL.Network;

/// <summary>Short lived, bounded cache and hedged reads for public resource search pages only.</summary>
internal static class ResourceListRequest
{
    private static readonly ConcurrentDictionary<string, (long Expires, string Json)> Cache = new();
    private static int _generation;

    public static void ClearCache()
    {
        Interlocked.Increment(ref _generation);
        Cache.Clear();
    }

    public static async Task<JsonObject> GetAsync(string url)
    {
        var generation = Volatile.Read(ref _generation);
        if (Cache.TryGetValue(url, out var cached) && cached.Expires > Environment.TickCount64)
        {
            LogWrapper.Info("[Comp] 列表缓存命中：" + url);
            return JsonNode.Parse(cached.Json)!.AsObject();
        }
        var mirror = ModDownload.DlSourceModGet(url);
        var preferMirror = BCCL.Core.App.Config.Download.Comp.CompSourceSolution == 0;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var timer = Stopwatch.StartNew();
        async Task<string> FetchAsync(string address, CancellationToken token)
        {
            var json = await Requester.FetchStringAsync(address, new RequestParam
            {
                Timeout = 12000, Retries = 1, UseBrowserUserAgent = true, CancellationToken = token
            }).ConfigureAwait(false);
            // Reject HTML/error JSON before it can win the race or enter the cache.
            var parsed = JsonNode.Parse(json)!.AsObject();
            if (parsed["hits"] is not JsonArray && (parsed["data"] is not JsonArray || parsed["pagination"] is null))
                throw new InvalidDataException("Invalid resource search response.");
            LogWrapper.Info($"[Comp] 列表响应 {timer.ElapsedMilliseconds} ms：{address}");
            return json;
        }

        var result = await HedgedResourceRequest.FetchAsync(
            token => FetchAsync(preferMirror ? mirror : url, token),
            token => FetchAsync(preferMirror ? url : mirror, token),
            TimeSpan.FromMilliseconds(350), budget.Token).ConfigureAwait(false);
        if (Cache.Count >= 128)
            foreach (var key in Cache.OrderBy(p => p.Value.Expires).Take(32).Select(p => p.Key))
                Cache.TryRemove(key, out _);
        if (generation == Volatile.Read(ref _generation))
            Cache[url] = (Environment.TickCount64 + 120000, result);
        return JsonNode.Parse(result)!.AsObject();
    }
}
