using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BCCL.Core.IO.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.IO;

[TestClass]
public class ResourceDownloadSmokeTest
{
    [TestMethod]
    [TestCategory("LiveDownload")]
    [DataRow("sodium", ".jar")]
    [DataRow("better-leaves", ".zip")]
    [Timeout(60000)]
    public async Task DownloadsAndVerifiesResourceWithRefusedLoopbackProxy(string project, string extension)
    {
        var output = Environment.GetEnvironmentVariable("BCCL_DOWNLOAD_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(output)) Assert.Inconclusive("Set BCCL_DOWNLOAD_SMOKE_DIR to run the live download check.");
        Directory.CreateDirectory(output!);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var fallback = new ResourceProxyFallbackHandler(new SocketsHttpHandler { UseProxy = false }, _ => true)
        {
            InnerHandler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            }
        };
        using var client = new HttpClient(fallback) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BCCL-DownloadVerification/1.0");
        var json = await client.GetStringAsync($"https://api.modrinth.com/v2/project/{project}/version?include_changelog=false");
        using var versions = JsonDocument.Parse(json);
        var candidates = versions.RootElement.EnumerateArray()
            .Where(version => project != "sodium" || version.GetProperty("game_versions").EnumerateArray().Any(v => v.GetString() == "1.21.1"))
            .SelectMany(version => version.GetProperty("files").EnumerateArray())
            .Where(file => file.GetProperty("filename").GetString()!.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
                           file.GetProperty("size").GetInt64() is > 0 and < 15000000)
            .OrderByDescending(file => file.TryGetProperty("primary", out var primary) && primary.GetBoolean())
            .ToList();
        Assert.IsTrue(candidates.Count > 0, $"No matching file returned for {project}");
        var file = candidates[0];
        var name = Path.GetFileName(file.GetProperty("filename").GetString()!);
        var url = file.GetProperty("url").GetString()!;
        var bytes = await client.GetByteArrayAsync(url);
        Assert.AreEqual(file.GetProperty("size").GetInt64(), bytes.LongLength);
        var digest = Convert.ToHexString(SHA512.HashData(bytes));
        Assert.IsTrue(digest.Equals(file.GetProperty("hashes").GetProperty("sha512").GetString(), StringComparison.OrdinalIgnoreCase));
        await File.WriteAllBytesAsync(Path.Combine(output!, name), bytes);
        await File.WriteAllTextAsync(Path.Combine(output!, project + "-verified.json"), JsonSerializer.Serialize(new
        {
            Project = project, Filename = name, Url = url, Size = bytes.LongLength,
            SHA512 = digest, Scenario = "Real loopback connection refused; production fallback handler; direct API and CDN download"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
