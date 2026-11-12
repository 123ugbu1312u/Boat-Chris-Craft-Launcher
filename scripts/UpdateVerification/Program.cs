using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using BCCL;
using BCCL.Core.Utils;

var config = typeof(BCCL.Core.App.Configuration.ConfigService);
const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
config.GetMethod("_InitializeConfigItems", hidden)!.Invoke(null, null);
config.GetField("_isConfigItemsInitialized", hidden)!.SetValue(null, true);
var source = new UpdatesGitCodeModel();
var release = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
if (release.VersionName != "0.0.1-alpha" || release.Sha256.Length != 64)
    throw new Exception("Unexpected public release metadata");
if (!source.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("0.0.2-alpha"), 0))
    throw new Exception("Older release was incorrectly offered as an update");
if (source.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("0.0.0-alpha"), 0))
    throw new Exception("Newer release was not detected");
var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "verification-download.exe"));
var loader = source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, output).Single();
var field = loader.GetType().GetField("loadDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!;
((Delegate)field.GetValue(loader)!).DynamicInvoke(new object?[] { null });
using var file = File.OpenRead(output);
var digest = Convert.ToHexString(SHA256.HashData(file));
if (!string.Equals(digest, release.Sha256, StringComparison.OrdinalIgnoreCase))
    throw new Exception("Release checksum did not match downloaded file");
Console.WriteLine($"PASS: live GitCode API, alpha comparisons, anonymous download and SHA-256: {release.VersionName}");
