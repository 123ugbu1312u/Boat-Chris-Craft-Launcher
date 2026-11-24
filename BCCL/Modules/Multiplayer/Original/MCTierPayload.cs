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
        EnsureAvailable(cacheRoot, OpenBundledResource, progress);

    private static Stream? OpenBundledResource(string name)
    {
        var assembly = typeof(MCTierPayload).Assembly;
        var embedded = assembly.GetManifestResourceStream(name);
        if (embedded is not null || name != ArchiveResource || string.IsNullOrEmpty(assembly.Location))
            return embedded;

        // IncludeAllContentForSelfExtract places this entry beside the extracted managed assembly.
        var path = Path.Combine(Path.GetDirectoryName(assembly.Location)!, ArchiveResource);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

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
        var destination = Path.Combine(cacheRoot,
            (manifest.CompatiblePackageSha256 ?? manifest.PackageSha256).ToLowerInvariant());
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
            // Restore the executable before repairing files stored as ranges inside it.
            var pending = manifest.Files.Where(file => !Matches(ResolvePath(destination, file.Path), file))
                .OrderBy(file => file.EntryPointOffset.HasValue).ToArray();
            if (pending.Length == 0) return ResolvePath(destination, manifest.EntryPoint);

            progress?.Report("首次使用或组件需要修复，正在准备联机…");
            using var archiveStream = openResource(ArchiveResource)
                ?? throw new InvalidDataException("内置联机资源不完整，请重新下载启动器。");
            if (!archiveStream.CanSeek) throw new InvalidDataException("无法读取内置联机资源。");
            if (!Convert.ToHexString(SHA256.HashData(archiveStream)).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("内置联机资源校验失败，请重新下载启动器。");
            archiveStream.Position = 0;
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            var expected = manifest.Files.Where(file => !file.EntryPointOffset.HasValue)
                .ToDictionary(file => file.ArchivePath, StringComparer.OrdinalIgnoreCase);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                ValidateRelativePath(entry.FullName);
                if (!expected.TryGetValue(entry.FullName, out var file) ||
                    (!file.Brotli && file.Length != entry.Length) ||
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
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (file.EntryPointOffset is { } offset)
                        {
                            using var source = File.OpenRead(ResolvePath(destination, manifest.EntryPoint));
                            source.Position = offset;
                            CopyLength(source, output, file.Length);
                        }
                        else
                        {
                            using var input = entries[file.ArchivePath].Open();
                            using var decoded = file.Brotli ? new BrotliStream(input, CompressionMode.Decompress) : null;
                            var content = decoded ?? input;
                            CopyLength(content, output, file.Length);
                            if (content.ReadByte() != -1)
                                throw new InvalidDataException("联机资源文件超出清单长度。");
                        }
                    }
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

    private static void CopyLength(Stream source, Stream target, long length)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (length > 0)
            {
                var count = (int)Math.Min(length, buffer.Length);
                source.ReadExactly(buffer.AsSpan(0, count));
                target.Write(buffer, 0, count);
                length -= count;
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("联机资源文件短于清单长度。", ex);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateManifest(Manifest manifest)
    {
        if (manifest.SchemaVersion is not (1 or 2) || !IsHash(manifest.PackageSha256) || manifest.EntryPoint != "MCTier.exe" ||
            (manifest.CompatiblePackageSha256 is { } cacheKey && (manifest.SchemaVersion != 2 || !IsHash(cacheKey))) ||
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

        var entryPoint = manifest.Files.Single(file => file.Path.Equals(manifest.EntryPoint, StringComparison.OrdinalIgnoreCase));
        var archiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if ((manifest.SchemaVersion == 1 && (file.Brotli || file.EntryPointOffset.HasValue)) ||
                (file.EntryPointOffset is { } offset &&
                 (file == entryPoint || file.Brotli || offset < 0 || offset > entryPoint.Length - file.Length)) ||
                (!file.EntryPointOffset.HasValue && !archiveNames.Add(file.ArchivePath)))
                throw new InvalidDataException("联机组件压缩清单无效。");
        }
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
        public string? CompatiblePackageSha256 { get; set; }
        public string EntryPoint { get; set; } = "";
        public PayloadFile[] Files { get; set; } = [];
    }

    private sealed class PayloadFile
    {
        public string Path { get; set; } = "";
        public long Length { get; set; }
        public string Sha256 { get; set; } = "";
        public bool Brotli { get; set; }
        public long? EntryPointOffset { get; set; }
        public string ArchivePath => Brotli ? Path + ".br" : Path;
    }
}
