using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.Link.MCTier;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Link;

[TestClass]
public class MCTierControlPipeTest
{
    [TestMethod]
    public async Task PrivatePipeChecksBothEndpointProcessesAndRejectsImpostors()
    {
        var name = "BCCL-MCTier-test-" + Guid.NewGuid().ToString("N");
        using var server = MCTierControlPipe.CreateServer(name);
        using var client = MCTierControlPipe.CreateClient(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        MCTierControlPipe.VerifyClient(server, Environment.ProcessId);
        MCTierControlPipe.VerifyServer(client, Environment.ProcessId);
        Assert.ThrowsExactly<IOException>(() => MCTierControlPipe.VerifyClient(server, int.MaxValue));
        Assert.ThrowsExactly<IOException>(() => MCTierControlPipe.VerifyServer(client, int.MaxValue));
        await client.WriteAsync(new byte[] { 1, 2, 3 }, timeout.Token);
        var received = new byte[3]; await server.ReadExactlyAsync(received, timeout.Token);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, received);
    }
}
