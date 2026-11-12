using System.Linq;
using System.Threading.Tasks;
using BCCL.Core.Utils.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace BCCL.Core.Test;
[TestClass]
public class FrameSignalTest
{
    [TestMethod] public async Task IdleTicksDoNotAccumulateIntoUnboundedWork() {
        using var signal=new AsyncCountResetEvent();
        for(var i=0;i<10000;i++) signal.Pulse();
        var next=signal.WaitAsync(); Assert.IsFalse(next.IsCompleted);
        signal.Pulse(); await next.WaitAsync(System.TimeSpan.FromSeconds(2));
    }
    [TestMethod] public async Task FrameWakesAllActiveWorkersOnce() {
        using var signal=new AsyncCountResetEvent();
        var workers=Enumerable.Range(0,32).Select(_=>signal.WaitAsync()).ToArray();
        signal.Pulse(); await Task.WhenAll(workers).WaitAsync(System.TimeSpan.FromSeconds(2));
        var next=signal.WaitAsync(); Assert.IsFalse(next.IsCompleted);signal.Pulse(); await next;
    }
    [TestMethod] public async Task OriginalCountedSignalStillPreservesPermits() {
        using var signal=new AsyncCountResetEvent();signal.Set(2);await signal.WaitAsync();await signal.WaitAsync();
        var next=signal.WaitAsync();Assert.IsFalse(next.IsCompleted);signal.Set();await next;
    }
}
