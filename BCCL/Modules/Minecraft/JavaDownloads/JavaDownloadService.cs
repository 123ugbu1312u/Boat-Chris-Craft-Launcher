using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BCCL.JavaDownloads;

public enum JavaVendor { Oracle, OpenJdk }
public sealed record JavaPackage(string Url, string Sha256);
public sealed class OracleDownloadUnavailableException(int version) : Exception(
    $"Oracle 当前未提供 Java {version} 的免登录 Windows x64 ZIP 下载。此版本可能需要在官网登录并接受条款；也可以选择 OpenJDK 自动安装。");

public static class JavaDownloadService
{
    private static readonly HttpClient Client = CreateClient();
    public const string OraclePage = "https://www.oracle.com/java/technologies/downloads/";
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BCCL/0.2 JavaDownloader");
        return client;
    }

    private static async Task<string> Metadata(string url, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        return await Client.GetStringAsync(url, deadline.Token).ConfigureAwait(false);
    }

    public static async Task<int[]> GetVersionsAsync(CancellationToken token)
    {
        // The catalog is refreshed rather than capped at a compiled-in latest major.
        var versions = new HashSet<int>();
        Exception? failure = null;
        try
        {
            using var info = JsonDocument.Parse(await Metadata("https://api.adoptium.net/v3/info/available_releases", token));
            foreach (var version in info.RootElement.GetProperty("available_releases").EnumerateArray())
                versions.Add(version.GetInt32());
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { failure = ex; }
        try
        {
            var html = await Metadata(OraclePage, token);
            foreach (Match match in Regex.Matches(html, @"https://download\.oracle\.com/java/(\d+)/"))
                versions.Add(int.Parse(match.Groups[1].Value));
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { failure = ex; }
        if (versions.Count == 0) throw new HttpRequestException("无法刷新 Java 官方版本列表，请检查网络后重试。", failure);
        versions.UnionWith(new[] { 8, 11, 17, 21, 25, 26, 27 });
        return versions.Where(v => v >= 8).OrderBy(v => v).ToArray();
    }

    public static async Task<JavaPackage> ResolveAsync(int version, JavaVendor vendor, CancellationToken token)
    {
        if (version < 8) throw new ArgumentOutOfRangeException(nameof(version));
        if (vendor == JavaVendor.Oracle)
        {
            var html = await Metadata(OraclePage, token);
            var match = Regex.Match(html, $@"https://download\.oracle\.com/java/{version}/latest/jdk-[^""<>\s]+_windows-x64_bin\.zip");
            if (!match.Success) throw new OracleDownloadUnavailableException(version);
            var checksum = await Metadata(match.Value + ".sha256", token);
            var hash = Regex.Match(checksum, @"\b[0-9a-fA-F]{64}\b");
            if (!hash.Success) throw new InvalidDataException("Oracle 未返回有效的 SHA-256 校验值。");
            return new JavaPackage(match.Value, hash.Value);
        }
        var json = await Metadata($"https://api.adoptium.net/v3/assets/latest/{version}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse", token);
        using var assets = JsonDocument.Parse(json);
        foreach (var asset in assets.RootElement.EnumerateArray())
        {
            var package = asset.GetProperty("binary").GetProperty("package");
            var url = package.GetProperty("link").GetString()!;
            var hash = package.GetProperty("checksum").GetString()!;
            if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$"))
                return new JavaPackage(url, hash);
        }
        throw new InvalidDataException($"OpenJDK 暂未发布 Java {version} 的 Windows x64 完整 JDK ZIP，请稍后刷新。");
    }

    public static async Task InstallAsync(JavaPackage package, string destination, IProgress<string> progress, CancellationToken token)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("目标文件夹已存在，请选择其他位置，避免覆盖已有 Java。");
        var parent = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(parent, ".bccl-java-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var zip = Path.Combine(staging, "download.zip");
            using (var response = await Client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var file = File.Create(zip);
                var buffer = new byte[131072];
                long total = 0;
                var expected = response.Content.Headers.ContentLength;
                var lastReport = Environment.TickCount64;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) != 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    total += read;
                    if (Environment.TickCount64 - lastReport > 200)
                    {
                        progress.Report(expected is > 0 ? $"正在下载：{total * 100d / expected:F1}%" : $"已下载 {total / 1048576d:F1} MB");
                        lastReport = Environment.TickCount64;
                    }
                }
                if (expected.HasValue && expected.Value != total) throw new IOException("Java 压缩包下载不完整。");
            }
            progress.Report("正在校验完整性…");
            await using (var file = File.OpenRead(zip))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Java 压缩包 SHA-256 校验失败，请重试。");
            }
            progress.Report("正在解压完整 JDK…");
            var unpacked = Path.Combine(staging, "unpacked");
            Directory.CreateDirectory(unpacked);
            await Task.Run(() => Extract(zip, unpacked, token), token).ConfigureAwait(false);
            var roots = Directory.GetDirectories(unpacked);
            if (roots.Length != 1 || !File.Exists(Path.Combine(roots[0], "bin", "java.exe")) ||
                !File.Exists(Path.Combine(roots[0], "bin", "javac.exe")))
                throw new InvalidDataException("下载内容不是完整的 Windows JDK，缺少 bin/java.exe 或 javac.exe。");
            token.ThrowIfCancellationRequested();
            Directory.Move(roots[0], destination);
            progress.Report("安装完成：" + destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    internal static void Extract(string archivePath, string destination, CancellationToken token)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(root, name));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || name.Contains(':') ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Java 压缩包包含非法路径。");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, false);
        }
    }
}
