using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BCCL.Core.Utils;

namespace BCCL;

/// <summary>Updates published by the BCCL owner, including alpha releases.</summary>
public sealed class UpdatesGitCodeModel : UpdatesGitReleaseModel
{
    public UpdatesGitCodeModel() : base("https://api.gitcode.com/api/v5/repos/BoatMinecraft/BoatChrisCraftLauncher/releases?per_page=100",
        "https://gitcode.com/BoatMinecraft/BoatChrisCraftLauncher", "BCCL GitCode") { }
}

public sealed class UpdatesGitHubModel : UpdatesGitReleaseModel
{
    public UpdatesGitHubModel() : base("https://api.github.com/repos/123ugbu1312u/Boat-Chris-Craft-Launcher/releases?per_page=100",
        "https://github.com/123ugbu1312u/Boat-Chris-Craft-Launcher", "BCCL GitHub") { }
}

public class UpdatesGitReleaseModel : IUpdateSource
{
    private readonly string apiUrl;
    private readonly Uri repository;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private VersionDataModel? selected;
    public string SourceName { get; set; }

    protected UpdatesGitReleaseModel(string apiUrl, string repositoryUrl, string sourceName)
    {
        this.apiUrl = apiUrl;
        repository = new Uri(repositoryUrl);
        SourceName = sourceName;
    }

    public bool IsAvailable() => true;
    public bool RefreshCache() { selected = FetchRelease(); return true; }
    public VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch)
    {
        if (arch != UpdateArch.x64) throw new NotSupportedException("此更新源暂未提供 ARM64 更新包");
        return selected = FetchRelease();
    }
    public bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode) =>
        SemVer.Parse(GetLatestVersion(channel, arch).VersionName) <= currentVersion;
    public VersionAnnouncementDataModel GetAnnouncementList() => new() { Content = new() };

    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output)
    {
        var release = selected ?? GetLatestVersion(channel, arch);
        return new List<ModLoader.LoaderBase>
        {
            new ModLoader.LoaderTask<int, int>("下载 BCCL 更新", _ =>
            {
                var temp = output + ".download";
                try
                {
                    using var response = Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    using var input = response.Content.ReadAsStream();
                    using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(file);
                    using var stream = File.OpenRead(temp);
                    var hash = Convert.ToHexString(SHA256.HashData(stream));
                    if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("更新包 SHA-256 校验失败");
                    stream.Dispose();
                    File.Move(temp, output, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            })
        };
    }

    private VersionDataModel FetchRelease()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.ParseAdd("BCCL-Updater/1.0");
        using var response = Client.Send(request);
        response.EnsureSuccessStatusCode();
        var releases = JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()) as JsonArray
            ?? throw new InvalidDataException(SourceName + " 发行版信息无效");
        return SelectRelease(releases);
    }

    internal VersionDataModel SelectRelease(JsonArray releases)
    {
        VersionDataModel? best = null;
        foreach (var item in releases)
        {
            if (item?["draft"]?.GetValue<bool>() == true) continue;
            var tag = item?["tag_name"]?.GetValue<string>() ?? "";
            var name = tag.StartsWith('v') ? tag[1..] : tag;
            if (!SemVer.TryParse(name, out var version)) continue;
            if (best is not null && version <= SemVer.Parse(best.VersionName)) continue;
            var assets = item?["assets"] as JsonArray;
            var asset = assets?.FirstOrDefault(a => a?["name"]?.GetValue<string>() is
                "Boat Chris Craft Launcher.exe" or "Boat.Chris.Craft.Launcher.exe");
            var digest = asset?["digest"]?.GetValue<string>() ?? "";
            var address = asset?["browser_download_url"]?.GetValue<string>() ?? "";
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71 ||
                !digest[7..].All(Uri.IsHexDigit) || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, repository.Host, StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith(repository.AbsolutePath + "/releases/download/", StringComparison.Ordinal)) continue;
            best = new VersionDataModel { VersionName = name, Sha256 = digest[7..].ToUpperInvariant(),
                DownloadUrl = address, Changelog = item?["body"]?.GetValue<string>() ?? "", Source = SourceName };
        }
        return best ?? throw new InvalidDataException("没有找到包含有效校验值的 BCCL 更新包");
    }
}
