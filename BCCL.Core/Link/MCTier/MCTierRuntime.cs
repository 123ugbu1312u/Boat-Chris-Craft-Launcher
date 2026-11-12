using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.App;
using BCCL.Core.Link.EasyTier;

namespace BCCL.Core.Link.MCTier;

public static class MCTierRuntime
{
    public const string Version = "2.6.4";
    public static string DirectoryPath => Path.Combine(Paths.SharedLocalData, "MCTier", "Runtime", Version);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string[] Files = ["easytier-core.exe", "easytier-cli.exe", "Packet.dll", "WinDivert64.sys", "wintun.dll"];
    public static bool IsInstalled => Files.All(f => File.Exists(Path.Combine(DirectoryPath, f)));

    public static async Task EnsureAsync(Action<string> progress, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            if (IsInstalled) return;
            Directory.CreateDirectory(DirectoryPath);
            // Reuse the launcher's installed EasyTier; keep MCTier runtime files isolated.
            if (File.Exists(Path.Combine(ETInfoProvider.ETPath, "easytier-core.exe")))
                foreach (var file in Files)
                {
                    var source = Path.Combine(ETInfoProvider.ETPath, file);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(DirectoryPath, file), true);
                }
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BCCL-MCTier/1.0");
            if (!Files.Take(4).All(f => File.Exists(Path.Combine(DirectoryPath, f))))
            {
                var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
                var arch = arm ? "arm64" : "x86_64";
                progress("正在从 EasyTier 官方下载联机组件，首次准备可能需要几分钟…");
                await DownloadZipAsync(http,
                    $"https://github.com/EasyTier/EasyTier/releases/download/v{Version}/easytier-windows-{arch}-v{Version}.zip",
                    arm ? "37023f8a3451c9234b17ee2089a03dc344ce90d803b5b359cb6c46682b0549b4" : "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84",
                    entry => Files.Contains(entry.Name) ? entry.Name : null, token);
            }
            if (!File.Exists(Path.Combine(DirectoryPath, "wintun.dll")))
            {
                progress("正在准备虚拟网卡组件…");
                var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
                await DownloadZipAsync(http, "https://www.wintun.net/builds/wintun-0.14.1.zip",
                    "07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51",
                    entry => entry.FullName == $"wintun/bin/{arch}/wintun.dll" ? "wintun.dll" : null, token);
            }
            if (!IsInstalled) throw new IOException("联机组件不完整，请重新准备组件。");
        }
        finally { Gate.Release(); }
    }

    private static async Task DownloadZipAsync(HttpClient http, string url, string sha256,
        Func<ZipArchiveEntry, string?> select, CancellationToken token)
    {
        var temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(temp);
                await response.Content.CopyToAsync(output, token);
            }
            await using (var input = File.OpenRead(temp))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("联机组件校验失败，请重试下载。");
            using var zip = ZipFile.OpenRead(temp);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var name = select(entry);
                if (name is null) continue;
                var destination = Path.Combine(DirectoryPath, name);
                var stage = destination + ".tmp";
                try
                {
                    entry.ExtractToFile(stage, true);
                    File.Move(stage, destination, true);
                }
                finally { if (File.Exists(stage)) File.Delete(stage); }
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
