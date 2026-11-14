using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BCCL.Multiplayer.Original;

var root = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(Path.GetTempPath(), "BCCL-payload-verification-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
var contents = new Dictionary<string, byte[]> {
    ["MCTier.exe"] = Encoding.UTF8.GetBytes("verification fixture, not an executable"),
    ["runtime/driver.sys"] = Encoding.UTF8.GetBytes("test driver data"),
    ["licenses/LICENSE"] = Encoding.UTF8.GetBytes("test license text")
};
(byte[] Archive, byte[] Manifest) MakePayload(Dictionary<string, byte[]> source, string? manifestHash = null,
    string? extraEntry = null, string? manifestPath = null)
{
    using var output = new MemoryStream();
    using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
    {
        foreach (var (name, bytes) in source)
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(bytes);
        }
        if (extraEntry != null) { using var entry = zip.CreateEntry(extraEntry).Open(); entry.WriteByte(1); }
    }
    var archive = output.ToArray();
    var manifest = JsonSerializer.SerializeToUtf8Bytes(new {
        schemaVersion = 1, packageSha256 = manifestHash ?? Hash(archive), entryPoint = "MCTier.exe",
        files = source.Select(file => new { path = manifestPath ?? file.Key, length = file.Value.Length, sha256 = Hash(file.Value) }).ToArray()
    });
    return (archive, manifest);
}
Func<string, Stream?> Open((byte[] Archive, byte[] Manifest) payload, Action? archiveRead = null) => name => {
    if (name == MCTierPayload.ManifestResource) return new MemoryStream(payload.Manifest, false);
    if (name != MCTierPayload.ArchiveResource) return null;
    archiveRead?.Invoke();
    return new MemoryStream(payload.Archive, false);
};
void Reject((byte[] Archive, byte[] Manifest) payload, string directory, string name)
{
    try { MCTierPayload.EnsureAvailable(Path.Combine(root, directory), Open(payload)); throw new Exception("Accepted invalid payload: " + name); }
    catch (InvalidDataException) { checks++; Console.WriteLine("PASS: " + name); }
}

var valid = MakePayload(contents);
var cache = Path.Combine(root, "中文 path with spaces");
var exe = MCTierPayload.EnsureAvailable(cache, Open(valid));
Check(File.ReadAllBytes(exe).SequenceEqual(contents["MCTier.exe"]), "first run extracts the embedded executable in a Chinese/spaced path");
Check(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "runtime/driver.sys")), "runtime and license directories are installed");
var before = File.GetLastWriteTimeUtc(exe);
var reused = MCTierPayload.EnsureAvailable(cache, Open(valid, () => throw new Exception("unnecessary re-extraction")));
Check(reused == exe && File.GetLastWriteTimeUtc(exe) == before, "healthy cache reuses validated files without opening the archive");
File.WriteAllText(exe, "corrupt");
File.Delete(Path.Combine(Path.GetDirectoryName(exe)!, "licenses/LICENSE"));
MCTierPayload.EnsureAvailable(cache, Open(valid));
Check(File.ReadAllBytes(exe).SequenceEqual(contents["MCTier.exe"]) &&
      File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "licenses/LICENSE")), "corrupted and missing files are repaired from embedded bytes");
var concurrentCache = Path.Combine(root, "concurrent");
var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => MCTierPayload.EnsureAvailable(concurrentCache, Open(valid)))));
Check(concurrent.Distinct().Count() == 1 && File.ReadAllBytes(concurrent[0]).SequenceEqual(contents["MCTier.exe"]), "concurrent installs converge on one valid runtime");
var upgradedContents = new Dictionary<string, byte[]>(contents) { ["MCTier.exe"] = Encoding.UTF8.GetBytes("next version fixture") };
var upgraded = MCTierPayload.EnsureAvailable(cache, Open(MakePayload(upgradedContents)));
Check(upgraded != exe && File.ReadAllBytes(exe).SequenceEqual(contents["MCTier.exe"]), "new versions do not overwrite the previous runtime");
Reject(MakePayload(contents, new string('0', 64)), "wrong-hash", "corrupted embedded archive is rejected");
Reject(MakePayload(contents, extraEntry: "../escaped.exe"), "zip-slip", "archive traversal entries are rejected");
Reject(MakePayload(contents, extraEntry: "unknown.dll"), "unknown", "unexpected executable-search-path files are rejected");
Reject(MakePayload(contents, extraEntry: "MCTier.exe"), "duplicate", "duplicate archive entries are rejected");
Reject(MakePayload(contents, manifestPath: "../escaped.exe"), "manifest-slip", "manifest traversal is rejected");
try { MCTierPayload.EnsureAvailable(Path.Combine(root, "missing"), _ => null); throw new Exception("Accepted missing resources"); }
catch (InvalidDataException) { checks++; Console.WriteLine("PASS: missing embedded resources fail clearly"); }
Check(!Directory.EnumerateFiles(root, "*.pending-*", SearchOption.AllDirectories).Any(), "no partial payload files remain");
Console.WriteLine($"All {checks} payload verification checks passed.");
