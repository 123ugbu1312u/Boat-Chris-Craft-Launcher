using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BCCL;
using BCCL.Network;

var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "../../Temp/vanilla-download-verification");
Directory.CreateDirectory(output);
var results = new List<object>();
var bytes = new byte[20 * 1024 * 1024];
new Random(739).NextBytes(bytes);
var hash = Convert.ToHexString(SHA1.HashData(bytes));
var url = "https://download-verification.invalid/client.jar";
using (var handler = new ControlledHandler(bytes))
using (var client = new HttpClient(handler))
{
    NetworkService.Client = client;
    var file = new DownloadFile { Check = new ModBase.FileChecker(bytes.Length, hash) };
    var path = Path.Combine(output, "range-client.jar");
    await FileDownloader.DownloadAsync(url, path, trackedFile: file);
    Require(handler.RangeRequests == 8, "20 MiB should use 8 initial ranges");
    Require(file.Check.Check(path) is null && DownloadResourceManager.ActiveConnectionCount == 0, "Ranged file hash and released connections");
    results.Add(new { Test = "Range download", Ranges = handler.RangeRequests, SHA1 = hash, Passed = true });

    handler.IgnoreRange = true;
    path = Path.Combine(output, "sequential-fallback.jar");
    await FileDownloader.DownloadAsync(url, path, trackedFile: file);
    Require(file.Check.Check(path) is null, "Range fallback hash");
    results.Add(new { Test = "Unsupported Range fallback", Passed = true });

    handler.ReadDelay = 1000;
    path = Path.Combine(output, "cancelled.jar");
    using var cancelled = new CancellationTokenSource(80);
    try { await FileDownloader.DownloadAsync(url, path, cancellationToken: cancelled.Token, trackedFile: file); throw new Exception("Cancellation was ignored"); }
    catch (OperationCanceledException) { }
    Require(!File.Exists(path) && !File.Exists(path + ModNet.NetDownloadEnd) && DownloadResourceManager.ActiveConnectionCount == 0, "Cancellation cleanup");
    results.Add(new { Test = "Cancelled download cleanup", Passed = true });
}

if (args.Contains("--live"))
{
    using var live = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(60) };
    live.DefaultRequestHeaders.UserAgent.ParseAdd("BCCL-DownloadVerification/1.0");
    NetworkService.Client = live;
    var manifest = JsonNode.Parse(await live.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"))!;
    var version = manifest["versions"]!.AsArray().First(v => v!["id"]!.ToString() == "1.21.1")!;
    var metadata = JsonNode.Parse(await live.GetStringAsync(version["url"]!.ToString()))!;
    var clientInfo = metadata["downloads"]!["client"]!;
    var timer = Stopwatch.StartNew();
    await DownloadKnownAsync(clientInfo, Path.Combine(output, "minecraft-1.21.1-client.jar"), "client");
    results.Add(new { Test = "Live vanilla client", Version = "1.21.1", Bytes = clientInfo["size"]!.GetValue<long>(), ElapsedMs = timer.ElapsedMilliseconds, Passed = true });

    var assets = metadata["assetIndex"]!;
    var index = JsonNode.Parse(await live.GetStringAsync(assets["url"]!.ToString()))!;
    var assetFiles = index["objects"]!.AsObject().Select(p => p.Value!).DistinctBy(p => p["hash"]!.ToString())
        .Where(p => p["size"]!.GetValue<long>() < 150000).Take(16).ToArray();
    timer.Restart();
    await Task.WhenAll(assetFiles.Select(async asset =>
    {
        var sha = asset["hash"]!.ToString();
        var original = $"https://resources.download.minecraft.net/{sha[..2]}/{sha}";
        var check = new ModBase.FileChecker(asset["size"]!.GetValue<long>(), sha);
        var path = Path.Combine(output, "assets", sha);
        await FileDownloader.DownloadAsync(new[] { original.Replace("resources.download.minecraft.net", "bmclapi2.bangbang93.com/assets"), original },
            path, enableParallelChunks: false, trackedFile: new DownloadFile { Check = check });
        Require(check.Check(path) is null, "Live asset SHA1");
    }));
    results.Add(new { Test = "Live parallel vanilla assets", Files = assetFiles.Length, ElapsedMs = timer.ElapsedMilliseconds, Passed = true });

    var libraries = metadata["libraries"]!.AsArray().Select(p => p?["downloads"]?["artifact"])
        .Where(p => p is not null && p["url"]!.ToString().StartsWith("https://libraries.minecraft.net/") && p["size"]!.GetValue<long>() < 2000000)
        .Take(8).ToArray();
    await Task.WhenAll(libraries.Select((artifact, i) => DownloadKnownAsync(artifact!, Path.Combine(output, "libraries", $"{i}.jar"), "library")));
    results.Add(new { Test = "Live vanilla libraries", Files = libraries.Length, Passed = true });
}
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: " + results.Count + " downloader integration scenarios");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task DownloadKnownAsync(JsonNode item, string path, string kind)
{
    var original = item["url"]!.ToString();
    var mirror = kind == "library" ? original.Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven")
        : original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com");
    var check = new ModBase.FileChecker(item["size"]!.GetValue<long>(), item["sha1"]!.ToString());
    await FileDownloader.DownloadAsync(new[] { mirror, original }, path, trackedFile: new DownloadFile { Check = check });
    Require(check.Check(path) is null, "Live file SHA1");
}

sealed class ControlledHandler(byte[] data) : HttpMessageHandler
{
    public bool IgnoreRange;
    public int ReadDelay = 1;
    public int RangeRequests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var range = request.Headers.Range?.Ranges.First();
        if (range is not null) Interlocked.Increment(ref RangeRequests);
        var start = IgnoreRange ? 0 : range?.From ?? 0;
        var end = IgnoreRange ? data.Length - 1 : range?.To ?? data.Length - 1;
        var response = new HttpResponseMessage(range is not null && !IgnoreRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = new StreamContent(new ControlledStream(data, (int)start, (int)(end - start + 1), ReadDelay))
        };
        response.Content.Headers.ContentLength = end - start + 1;
        if (range is not null && !IgnoreRange) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, data.Length);
        return Task.FromResult(response);
    }
}
sealed class ControlledStream(byte[] data, int offset, int length, int delay) : MemoryStream(data, offset, length, false)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);
        return await base.ReadAsync(buffer, cancellationToken);
    }
}
