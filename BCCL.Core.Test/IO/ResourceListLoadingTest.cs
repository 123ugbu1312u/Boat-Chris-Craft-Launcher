using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.IO.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.IO;

[TestClass]
public class ResourceListLoadingTest
{
    [TestMethod]
    public async Task FastPreferredDoesNotStartFallback()
    {
        var called = false;
        var result = await HedgedResourceRequest.FetchAsync(_ => Task.FromResult(1), _ =>
        { called = true; return Task.FromResult(2); }, TimeSpan.FromSeconds(1), default);
        Assert.AreEqual(1, result);
        Assert.IsFalse(called);
    }

    [TestMethod]
    public async Task SlowPreferredIsCancelledWhenFallbackWins()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await HedgedResourceRequest.FetchAsync(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); }
            return 1;
        }, _ => Task.FromResult(2), TimeSpan.FromMilliseconds(10), default);
        Assert.AreEqual(2, result);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task FailedPreferredStartsFallbackImmediately()
    {
        var result = await HedgedResourceRequest.FetchAsync<int>(_ => Task.FromException<int>(new Exception("offline")),
            _ => Task.FromResult(2), TimeSpan.FromHours(1), default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task BothEndpointFailuresAreReported()
    {
        try
        {
            await HedgedResourceRequest.FetchAsync<int>(_ => Task.FromException<int>(new Exception("official")),
                _ => Task.FromException<int>(new Exception("mirror")), TimeSpan.Zero, default);
            Assert.Fail("Expected both errors");
        }
        catch (AggregateException ex) { Assert.HasCount(2, ex.InnerExceptions); }
    }

    [TestMethod]
    public async Task PendingSourceDoesNotBlockAndIsConsumedExactlyOnce()
    {
        var sources = new ProgressiveResourceSources<int[]>();
        var slow = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        sources.Start("slow", () => slow.Task);
        sources.Start("fast", () => Task.FromResult(new[] { 1, 2 }));
        var first = await sources.TakeReadyAsync(x => x.Length > 0, TimeSpan.FromMilliseconds(10))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.HasCount(1, first);
        Assert.AreEqual("fast", first[0].Source);
        Assert.IsTrue(sources.HasPending);
        sources.Start("slow", () => throw new Exception("Duplicate request"));
        slow.SetResult(new[] { 3 });
        var second = await sources.TakeReadyAsync(x => x.Length > 0, TimeSpan.Zero);
        Assert.HasCount(1, second);
        Assert.AreEqual(3, second[0].Value![0]);
        Assert.IsFalse(sources.HasPending);
        Assert.HasCount(0, await sources.TakeReadyAsync(x => true, TimeSpan.Zero));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyOrFailedFirstSourceWaitsForUsefulResults(bool fail)
    {
        var sources = new ProgressiveResourceSources<int[]>();
        var useful = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        sources.Start("first", () => fail ? Task.FromException<int[]>(new Exception("unavailable")) : Task.FromResult(Array.Empty<int>()));
        sources.Start("useful", () => useful.Task);
        var read = sources.TakeReadyAsync(x => x.Length > 0, TimeSpan.Zero);
        Assert.IsFalse(read.IsCompleted);
        useful.SetResult(new[] { 5 });
        var result = await read;
        Assert.HasCount(2, result);
        Assert.AreEqual(5, result.Single(r => r.Source == "useful").Value![0]);
    }

    [TestMethod]
    public async Task CompletedSourcesAreMergedAndCanStartNextPage()
    {
        var sources = new ProgressiveResourceSources<int>();
        sources.Start("a", () => Task.FromResult(1));
        sources.Start("b", () => Task.FromResult(2));
        Assert.HasCount(2, await sources.TakeReadyAsync(_ => true, TimeSpan.Zero));
        sources.Start("a", () => Task.FromResult(3));
        Assert.AreEqual(3, (await sources.TakeReadyAsync(_ => true, TimeSpan.Zero))[0].Value);
    }

    [TestMethod]
    public async Task CallerCancellationStopsWaiting()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        async Task<int> Never(CancellationToken token) { await Task.Delay(Timeout.Infinite, token); return 0; }
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => HedgedResourceRequest.FetchAsync(
            Never, Never, TimeSpan.Zero, cts.Token));
    }
}
