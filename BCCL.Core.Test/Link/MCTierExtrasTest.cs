using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.Link.MCTier;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Link;

[TestClass]
public class MCTierExtrasTest
{
    [TestMethod]
    public async Task LongPasswordAndItsInvitationAuthenticateTheSameExtrasNetwork()
    {
        var original = new MCTierRoom("long-password-test", new string('密', 1024) + "\0", MCTierRoom.DefaultNode);
        using var a = new MCTierExtrasHub(original, "A", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
        using var b = new MCTierExtrasHub(MCTierRoom.ParseInvite(original.ToInvite()), "B", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await a.ConnectDirectAsync("127.0.0.1", b.ListeningPort, timeout.Token);
        await Wait(() => a.Peers.Count == 1 && b.Peers.Count == 1, timeout.Token);
        Assert.AreEqual("B", a.Peers[0].Name);
    }
    private static async Task Wait(Func<bool> ready, CancellationToken token)
    { while (!ready()) await Task.Delay(25, token); }

    [TestMethod]
    public async Task RepeatedScreenFramesSurviveChatAndVoiceAndStopClearsTheCache()
    {
        var room = new MCTierRoom("repeat-test", "123", MCTierRoom.DefaultNode);
        using var a = new MCTierExtrasHub(room, "A", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
        using var b = new MCTierExtrasHub(room, "B", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var screens = 0; var stopped = false;
        b.Received += (_, packet) => { if (packet.Kind == "screen") Interlocked.Increment(ref screens); if (packet.Kind == "screen-stop") stopped = true; };
        await a.ConnectDirectAsync("127.0.0.1", b.ListeningPort, timeout.Token);
        await Wait(() => a.Peers.Count == 1 && b.Peers.Count == 1, timeout.Token);
        var frame = new ExtrasPacket("screen", Size: 1920, Offset: 1080, Data: [1, 2, 3]);
        await a.BroadcastScreenAsync(frame);
        await a.SendAsync("10.1.1.2", new ExtrasPacket("chat", Text: "画面保持"));
        await a.SendAsync("10.1.1.2", new ExtrasPacket("voice", Data: new byte[1280]));
        await a.BroadcastScreenAsync(frame);
        await Wait(() => screens == 2, timeout.Token);
        await a.BroadcastScreenAsync(frame with { Size = 1280 });
        await Wait(() => screens == 3, timeout.Token);
        await a.SendAsync("10.1.1.2", new ExtrasPacket("screen-stop"));
        await Wait(() => stopped, timeout.Token);
        await a.SendAsync("10.1.1.2", new ExtrasPacket("screen-repeat"));
        await Task.Delay(100, timeout.Token);
        Assert.AreEqual(3, screens, "A repeat marker cannot resurrect a stopped screen.");
    }

    [TestMethod]
    public async Task RoomMembersExchangeChatVoiceScreenAndConsentPackets()
    {
        var room = new MCTierRoom("test", "123", MCTierRoom.DefaultNode);
        using var a = new MCTierExtrasHub(room, "Host", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
        using var b = new MCTierExtrasHub(room, "Guest", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = new System.Collections.Concurrent.ConcurrentQueue<ExtrasPacket>();
        b.Received += (_, packet) => received.Enqueue(packet);
        await a.ConnectDirectAsync("127.0.0.1", b.ListeningPort, timeout.Token);
        await Wait(() => a.Peers.Count == 1 && b.Peers.Count == 1, timeout.Token);
        Assert.AreEqual("Host", b.Peers[0].Name);
        foreach (var packet in new[]
        {
            new ExtrasPacket("chat", Text: "中文消息"), new ExtrasPacket("voice", Data: new byte[1280]),
            new ExtrasPacket("screen", Size: 1920, Offset: 1080, Data: [1, 2, 3]),
            new ExtrasPacket("control-request"), new ExtrasPacket("control-grant", new string('a', 64)),
            new ExtrasPacket("input", new string('a', 64), "move", X: .2, Y: .8), new ExtrasPacket("control-revoke")
        }) await a.SendAsync("10.1.1.2", packet);
        await Wait(() => received.Count == 7, timeout.Token);
        Assert.AreEqual("中文消息", received.First().Text);
        a.Dispose();
        await Wait(() => b.Peers.Count == 0, timeout.Token);
    }

    [TestMethod]
    public async Task WrongPasswordCannotSendOrDiscoverMembers()
    {
        using var a = new MCTierExtrasHub(new MCTierRoom("test", "wrong", MCTierRoom.DefaultNode), "Intruder", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
        using var b = new MCTierExtrasHub(new MCTierRoom("test", "right", MCTierRoom.DefaultNode), "Host", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await a.ConnectDirectAsync("127.0.0.1", b.ListeningPort, timeout.Token);
        await Task.Delay(500, timeout.Token);
        Assert.AreEqual(0, a.Peers.Count); Assert.AreEqual(0, b.Peers.Count);
    }

    [TestMethod]
    public async Task FilesRequireAcceptanceAndArriveWithMatchingHash()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BCCL-extras-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var room = new MCTierRoom("test", "", MCTierRoom.DefaultNode);
            using var a = new MCTierExtrasHub(room, "A", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
            using var b = new MCTierExtrasHub(room, "B", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
            b.Notice += Console.WriteLine;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var bytes = RandomNumberGenerator.GetBytes(240001);
            var file = Path.Combine(folder, "测试文件.bin");
            await File.WriteAllBytesAsync(file, bytes, timeout.Token);
            await a.ShareFileAsync(file); // Offers must also replay to members connecting later.
            await a.ConnectDirectAsync("127.0.0.1", b.ListeningPort, timeout.Token);
            await Wait(() => b.Files.Count == 1, timeout.Token);
            var received = Path.Combine(folder, "received");
            Assert.IsFalse(Directory.Exists(received));
            await b.ReceiveFileAsync(b.Files[0], received);
            await Wait(() => Directory.GetFiles(received).Any(p => !p.EndsWith(".partial")), timeout.Token);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(Directory.GetFiles(received).Single(), timeout.Token));
            await b.ReceiveFileAsync(b.Files[0], received);
            await Wait(() => Directory.GetFiles(received).Count(p => !p.EndsWith(".partial")) == 2, timeout.Token);
            Assert.AreEqual(2, Directory.GetFiles(received).Length, "An existing file must never be overwritten.");
            await a.ClearSharedAsync();
            await Wait(() => b.Files.Count == 0, timeout.Token);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task CancellingFileReceptionCleansStagingAndKeepsChatConnected()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BCCL-cancel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var room = new MCTierRoom("test", "", MCTierRoom.DefaultNode);
            using var a = new MCTierExtrasHub(room, "A", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.1" };
            using var b = new MCTierExtrasHub(room, "B", IPAddress.Loopback, 0) { LocalAddress = "10.1.1.2" };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var file = Path.Combine(folder, "large.bin");
            await File.WriteAllBytesAsync(file, new byte[8 * 1024 * 1024], timeout.Token);
            await a.ShareFileAsync(file);
            await a.ConnectDirectAsync("127.0.0.1", b.ListeningPort, timeout.Token);
            await Wait(() => b.Files.Count == 1, timeout.Token);
            var received = Path.Combine(folder, "received");
            await b.ReceiveFileAsync(b.Files[0], received);
            await b.CancelReceivingAsync();
            await Task.Delay(200, timeout.Token);
            Assert.AreEqual(0, Directory.GetFiles(received).Length);
            var chatted = false;
            a.Received += (_, packet) => { if (packet.Kind == "chat") chatted = true; };
            await b.SendAsync("10.1.1.1", new ExtrasPacket("chat", Text: "取消文件不应断开聊天"));
            await Wait(() => chatted, timeout.Token);
            Assert.AreEqual(1, b.Peers.Count);
        }
        finally { Directory.Delete(folder, true); }
    }
}
