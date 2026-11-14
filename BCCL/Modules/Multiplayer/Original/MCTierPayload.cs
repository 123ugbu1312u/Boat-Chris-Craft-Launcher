using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BCCL.Multiplayer.Original;

/// <summary>Installs the verified, executable-embedded runtime without adjacent distribution files.</summary>
internal static class MCTierPayload
{
    internal const string ArchiveResource = "BCCL.Multiplayer.Original.bundle.zip";
    internal const string ManifestResource = "BCCL.Multiplayer.Original.bundle.json";

    internal static string EnsureAvailable(string cacheRoot, IProgress<string>? progress = null) =>
        EnsureAvailable(cacheRoot, typeof(MCTierPayload).Assembly.GetManifestResourceStream, progress);

    // Resource streams are injectable so verification exercises the same extraction and recovery code.
    internal static string EnsureAvailable(string cacheRoot, Func<string, Stream?> openResource,
        IProgress<string>? progress = null)
    {
        progress?.Report("正在检查联机组件…");
        using var manifestStream = openResource(ManifestResource)
            ?? throw new InvalidDataException("此启动器未包含完整联机组件，请重新下载完整版本。");
        if (manifestStream.Length > 1024 * 1024) throw new InvalidDataException("联机组件清单过大。");
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("联机组件清单无效。");
        ValidateManifest(manifest);
        cacheRoot = Path.GetFullPath(cacheRoot);
        var destination = Path.Combine(cacheRoot, manifest.PackageSha256.ToLowerInvariant());
        var lockKey = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(destination.ToUpperInvariant())));
        using var installLock = new Mutex(false, @"Local\BCCL.Multiplayer.Payload." + lockKey);
        var acquired = false;
        try
        {
            try { acquired = installLock.WaitOne(TimeSpan.FromMinutes(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("其他启动器正在准备联机组件，请稍后重试。");
            CreatePlainDirectory(cacheRoot);
            CreatePlainDirectory(destination);
            var pending = manifest.Files.Where(file => !Matches(ResolvePath(destination, file.Path), file)).ToArray();
            if (pending.Length == 0) return ResolvePath(destination, manifest.EntryPoint);

            progress?.Report("首次使用或组件需要修复，正在准备联机…");
            using var archiveStream = openResource(ArchiveResource)
                ?? throw new InvalidDataException("内置联机资源不完整，请重新下载启动器。");
            if (!archiveStream.CanSeek) throw new InvalidDataException("无法读取内置联机资源。");
            if (!Convert.ToHexString(SHA256.HashData(archiveStream)).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("内置联机资源校验失败，请重新下载启动器。");
            archiveStream.Position = 0;
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            var expected = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                ValidateRelativePath(entry.FullName);
                if (!expected.TryGetValue(entry.FullName, out var file) || file.Length != entry.Length ||
                    !entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException("联机资源目录与清单不一致。");
            }
            if (entries.Count != expected.Count) throw new InvalidDataException("联机资源文件缺失。");
            for (var index = 0; index < pending.Length; index++)
            {
                var file = pending[index];
                progress?.Report($"正在准备联机组件（{index + 1}/{pending.Length}）…");
                var target = ResolvePath(destination, file.Path);
                CreatePlainDirectory(Path.GetDirectoryName(target)!);
                CheckPlainPath(target);
                var temporary = target + ".pending-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var input = entries[file.Path].Open())
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output, 128 * 1024);
                    if (!Matches(temporary, file)) throw new InvalidDataException("联机组件文件校验失败。");
                    File.Move(temporary, target, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            return ResolvePath(destination, manifest.EntryPoint);
        }
        finally
        {
            if (acquired) installLock.ReleaseMutex();
        }
    }

    private static void ValidateManifest(Manifest manifest)
    {
        if (manifest.SchemaVersion != 1 || !IsHash(manifest.PackageSha256) || manifest.EntryPoint != "MCTier.exe" ||
            manifest.Files is not { Length: > 0 and <= 1000 })
            throw new InvalidDataException("联机组件清单格式无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path);
            if (!names.Add(file.Path) || !IsHash(file.Sha256) || file.Length < 0 || file.Length > 1024L * 1024 * 1024)
                throw new InvalidDataException("联机组件文件清单无效。");
            total += file.Length;
        }
        if (total > 2L * 1024 * 1024 * 1024 || !names.Contains(manifest.EntryPoint))
            throw new InvalidDataException("联机组件清单不完整。");
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || Path.IsPathRooted(path) ||
            path.Split('/').Any(part => part.Length == 0 || part is "." or ".." ||
                part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("联机组件路径无效。");
    }

    private static string ResolvePath(string root, string relative)
    {
        ValidateRelativePath(relative);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("联机组件路径越界。");
        return full;
    }

    private static void CheckPlainPath(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("联机组件缓存路径不能是目录链接，请更换缓存位置。");
    }

    private static void CreatePlainDirectory(string path)
    {
        CheckPlainPath(path);
        Directory.CreateDirectory(path);
        CheckPlainPath(path);
    }

    private static bool Matches(string path, PayloadFile file)
    {
        CheckPlainPath(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != file.Length) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Manifest
    {
        public int SchemaVersion { get; set; }
        public string PackageSha256 { get; set; } = "";
        public string EntryPoint { get; set; } = "";
        public PayloadFile[] Files { get; set; } = [];
    }

    private sealed class PayloadFile
    {
        public string Path { get; set; } = "";
        public long Length { get; set; }
        public string Sha256 { get; set; } = "";
    }
}
