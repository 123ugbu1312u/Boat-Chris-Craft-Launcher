using System;
using System.IO;
using System.Threading.Tasks;
using BCCL.Core.IO.Storage;
using BCCL.Core.Utils.Hash;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.IO;

[TestClass]
public sealed class HashStorageRegressionTest
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NonSeekableInputCanBeReadBackAndEmptyLegacyObjectCanBeRepaired(bool compressed)
    {
        var folder = Path.Combine(Path.GetTempPath(), "BCCL-HashStorage-" + Guid.NewGuid());
        var data = new byte[500000];
        new Random(428).NextBytes(data);
        try
        {
            var storage = new HashStorage(folder, SHA256Provider.Instance, compressed);
            using var input = new NonSeekableStream(data);
            var hash = await storage.PutAsync(input);
            using (var stored = storage.Get(hash!)!)
            using (var actual = new MemoryStream())
            {
                await stored.CopyToAsync(actual);
                CollectionAssert.AreEqual(data, actual.ToArray());
            }
            var path = Path.Combine(folder, hash![..2], hash);
            await File.WriteAllBytesAsync(path, []); // Simulate a corrupt object left by an older build.
            Assert.IsNull(storage.Get(hash));
            using var replacement = new MemoryStream(data);
            Assert.AreEqual(hash, await storage.PutAsync(replacement));
            using var repaired = storage.Get(hash)!;
            using var content = new MemoryStream();
            await repaired.CopyToAsync(content);
            CollectionAssert.AreEqual(data, content.ToArray());
            Assert.HasCount(1, Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}
