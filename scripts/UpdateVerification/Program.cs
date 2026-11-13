using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using BCCL;
using BCCL.Core.Utils;
using System.Text.Json.Nodes;

var config = typeof(BCCL.Core.App.Configuration.ConfigService);
const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
config.GetMethod("_InitializeConfigItems", hidden)!.Invoke(null, null);
config.GetField("_isConfigItemsInitialized", hidden)!.SetValue(null, true);
foreach (var source in new UpdatesGitReleaseModel[] { new UpdatesGitCodeModel(), new UpdatesGitHubModel() })
{
var repository = source is UpdatesGitHubModel
    ? "https://github.com/123ugbu1312u/Boat-Chris-Craft-Launcher"
    : "https://gitcode.com/BoatMinecraft/BoatChrisCraftLauncher";
var select = typeof(UpdatesGitReleaseModel).GetMethod("SelectRelease", BindingFlags.Instance | BindingFlags.NonPublic)!;
JsonObject Release(string version, string assetName = "Boat Chris Craft Launcher.exe", string? url = null,
    string? digest = null, bool draft = false) => new()
{
    ["tag_name"] = "v" + version, ["draft"] = draft,
    ["assets"] = new JsonArray(new JsonObject { ["name"] = assetName,
        ["browser_download_url"] = url ?? repository + "/releases/download/v" + version + "/launcher.exe",
        ["digest"] = digest ?? "sha256:" + new string('a', 64) })
};
var fixture = new JsonArray(Release("0.1.0-beta"), Release("0.1.1-alpha", "Boat.Chris.Craft.Launcher.exe"),
    Release("9.0.0", draft: true), Release("8.0.0", url: "https://example.com/launcher.exe"),
    Release("7.0.0", digest: "sha256:invalid"), Release("6.0.0", url: repository + "-other/releases/download/launcher.exe"));
var chosen = (VersionDataModel)select.Invoke(source, new object[] { fixture })!;
if (chosen.VersionName != "0.1.1-alpha" || chosen.Sha256.Length != 64)
    throw new Exception("Release selection, filename compatibility or validation failed");
try { source.GetLatestVersion(UpdateChannel.stable, UpdateArch.arm64); throw new Exception("Accepted x64 asset on ARM64"); }
catch (NotSupportedException) { }
var release = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
if (release.Sha256.Length != 64 || !release.DownloadUrl.StartsWith(repository + "/releases/download/"))
    throw new Exception("Unexpected public release metadata");
if (!source.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("99.0.0"), 0))
    throw new Exception("Older release was incorrectly offered as an update");
if (source.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("0.0.0-alpha"), 0))
    throw new Exception("Newer release was not detected");
Console.WriteLine($"PASS: {source.SourceName}: release validation, alpha versions, filenames, live API: {release.VersionName}");
if (!args.Contains("--download")) continue;
var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, source.SourceName + "-verification-download.exe"));
var loader = source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, output).Single();
var field = loader.GetType().GetField("loadDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!;
((Delegate)field.GetValue(loader)!).DynamicInvoke(new object?[] { null });
using var file = File.OpenRead(output);
var digest = Convert.ToHexString(SHA256.HashData(file));
if (!string.Equals(digest, release.Sha256, StringComparison.OrdinalIgnoreCase))
    throw new Exception("Release checksum did not match downloaded file");
Console.WriteLine($"PASS: {source.SourceName}: anonymous download and SHA-256: {release.VersionName}");
}

