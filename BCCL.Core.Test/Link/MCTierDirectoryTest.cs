using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.Link.MCTier;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Link;

[TestClass]
public class MCTierDirectoryTest
{
    [TestMethod]
    public void AllowsTlsAndLoopbackTestEndpointsButRejectsUntrustedSchemes()
    {
        Assert.AreEqual("wss", MCTierDirectoryLease.ValidateEndpoint(MCTierDirectoryLease.DefaultEndpoint).Scheme);
        Assert.IsTrue(MCTierDirectoryLease.ValidateEndpoint("ws://127.0.0.1:12345/").IsLoopback);
        foreach (var endpoint in new[] { "file:///C:/room", "https://example.com", "ws://example.com", "wss://user:password@example.com", "wss://example.com/#fragment" })
            Assert.ThrowsExactly<ArgumentException>(() => MCTierDirectoryLease.ValidateEndpoint(endpoint));
    }

    [TestMethod]
    public void OnlyExplicitOccupiedErrorsArePresentedAsDuplicateNames()
    {
        Assert.IsTrue(new MCTierDirectoryException("大厅名称已被占用，请更换大厅名称后重试").NameOccupied);
        Assert.IsFalse(new MCTierDirectoryException("名称检测超时").NameOccupied);
        Assert.IsFalse(new MCTierDirectoryException("密码错误").NameOccupied);
        Assert.IsFalse(new MCTierDirectoryException("大厅不存在或已关闭").NameOccupied);
    }

    [TestMethod]
    public async Task CancelledCheckIsNotReportedAsAnAvailableName()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var room = new MCTierRoom("BCCL-test", "", MCTierRoom.DefaultNode);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            using var lease = await MCTierDirectoryLease.ReserveAsync(room, "Test", new Uri("ws://127.0.0.1:9"), cancellation.Token);
        });
    }

    [TestMethod]
    public async Task InvalidHandshakeIsNotReportedAsAnAvailableName()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await connection.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
        });
        try
        {
            await Assert.ThrowsExactlyAsync<MCTierDirectoryException>(async () =>
            {
                using var lease = await MCTierDirectoryLease.ReserveAsync(new MCTierRoom("BCCL-test", "", MCTierRoom.DefaultNode),
                    "Test", new Uri($"ws://127.0.0.1:{port}/"), timeout.Token);
            });
            await server;
        }
        finally { listener.Stop(); }
    }
}
