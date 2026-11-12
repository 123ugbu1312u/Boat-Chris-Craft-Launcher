using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.IO.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.IO;

[TestClass]
public class MinecraftDownloadOptimizationTest
{
    private const string Official = "https://resources.download.minecraft.net/aa/file";
    private const string Mirror = "https://bmclapi2.bangbang93.com/assets/aa/file";

    [TestMethod]
    public async Task FastSourceWinsAndOtherProbesAreCancelled()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selector = new MinecraftDownloadSourceSelector(async (url, token) =>
        {
            if (url == Official) return;
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); }
        });
        var result = await selector.OrderAsync(new[] { Mirror, Official }, default);
        Assert.AreEqual(Official, result[0]);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        CollectionAssert.AreEquivalent(new[] { Official, Mirror }, result.ToArray());
    }

    [TestMethod]
    public async Task BatchCoalescesProbesAndReusesWinnerForOtherFiles()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var selector = new MinecraftDownloadSourceSelector(async (url, token) =>
        {
            Interlocked.Increment(ref calls);
            await ready.Task.WaitAsync(token);
            if (url.Contains("bmclapi2")) throw new Exception("mirror offline");
        });
        var batch = Enumerable.Range(0, 64).Select(_ => selector.OrderAsync(new[] { Mirror, Official }, default)).ToArray();
        ready.SetResult();
        await Task.WhenAll(batch);
        Assert.AreEqual(2, calls);
        var next = await selector.OrderAsync(new[] { Mirror + "2", Official + "2" }, default);
        Assert.AreEqual(Official + "2", next[0]);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task FailedSelectedSourceTriggersNewSelection()
    {
        var officialFails = false;
        var calls = 0;
        var selector = new MinecraftDownloadSourceSelector((url, _) =>
        {
            calls++;
            return url == Official && officialFails ? Task.FromException(new Exception("offline")) : Task.CompletedTask;
        });
        var urls = new[] { Official, Mirror };
        Assert.AreEqual(Official, (await selector.OrderAsync(urls, default))[0]);
        officialFails = true;
        selector.ReportFailure(urls, Official);
        Assert.AreEqual(Mirror, (await selector.OrderAsync(urls, default))[0]);
        Assert.AreEqual(4, calls);
    }

    [TestMethod]
    public async Task FailedProbesKeepOriginalFallbacks()
    {
        var selector = new MinecraftDownloadSourceSelector((_, _) => Task.FromException(new Exception("offline")));
        var urls = new[] { Mirror, Official };
        CollectionAssert.AreEqual(urls, (await selector.OrderAsync(urls, default)).ToArray());
    }

    [TestMethod]
    [DataRow("https://api.minecraftservices.com/profile")]
    [DataRow("https://resources.download.minecraft.net.attacker.test/file")]
    [DataRow("http://resources.download.minecraft.net/aa/file")]
    [DataRow("https://cdn.modrinth.com/mod.jar")]
    public async Task NonVanillaRequestsAreNotProbed(string url)
    {
        var selector = new MinecraftDownloadSourceSelector((_, _) => throw new AssertFailedException("Unexpected probe"));
        var urls = new[] { url, Mirror };
        CollectionAssert.AreEqual(urls, (await selector.OrderAsync(urls, default)).ToArray());
    }

    [TestMethod]
    public async Task CancelledWaiterDoesNotCancelOtherBatchFiles()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selector = new MinecraftDownloadSourceSelector(async (_, token) => await ready.Task.WaitAsync(token));
        using var cancelled = new CancellationTokenSource();
        var first = selector.OrderAsync(new[] { Official, Mirror }, cancelled.Token);
        var second = selector.OrderAsync(new[] { Official, Mirror }, default);
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        ready.SetResult();
        Assert.HasCount(2, await second);
    }

    [TestMethod]
    [DataRow(1L, 8)]
    [DataRow(4194304L, 8)]
    [DataRow(20971520L, 8)]
    [DataRow(30000001L, 4)]
    [DataRow(10737418240L, 8)]
    public void RangesCoverEachByteExactlyOnce(long size, int workers)
    {
        var ranges = DownloadRangePlan.Create(size, workers);
        Assert.IsTrue(ranges.Count is > 0 and <= 1024);
        long next = 0;
        foreach (var range in ranges)
        {
            Assert.AreEqual(next, range.Start);
            Assert.IsTrue(range.End >= range.Start);
            next = range.End + 1;
        }
        Assert.AreEqual(size, next);
        if (size == 20971520L) Assert.HasCount(8, ranges);
    }
}
