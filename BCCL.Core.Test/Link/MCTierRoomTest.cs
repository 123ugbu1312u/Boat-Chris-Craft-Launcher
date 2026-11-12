using System;
using System.Linq;
using BCCL.Core.Link.MCTier;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Link;

[TestClass]
public class MCTierRoomTest
{
    [TestMethod]
    public void PasswordsHaveNoFormatOrUserLengthRequirement()
    {
        foreach (var password in new[] { "", "1", "a", "!", "  中文 😀  ", new string('x', 256), new string('密', 1024) + "\0\n" })
        {
            var room = new MCTierRoom("test", password, MCTierRoom.DefaultNode);
            room.Validate();
            var imported = MCTierRoom.ParseInvite(room.ToInvite());
            Assert.AreEqual(room.NetworkPassword, imported.NetworkPassword);
            Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(room.NetworkPassword) <= 256);
            Assert.IsFalse(room.NetworkPassword.Any(char.IsControl));
        }
        Assert.AreEqual("  中文 😀  ", new MCTierRoom("test", "  中文 😀  ", MCTierRoom.DefaultNode).NetworkPassword);
        var longRoom = new MCTierRoom("test", new string('密', 1024), MCTierRoom.DefaultNode);
        Assert.AreNotEqual(longRoom.NetworkPassword, (longRoom with { Password = longRoom.Password + "!" }).NetworkPassword);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void CreatingAlwaysRequestsElevationRegardlessOfNetworkMode(bool creating, bool adapter)
    {
        var start = MCTierHelperLaunch.Create("BCCL.exe", "C:/BCCL", "BCCL-MCTier-test", 1, 2, creating, adapter);
        Assert.AreEqual(creating || adapter, start.UseShellExecute);
        Assert.AreEqual(creating || adapter ? "runas" : "", start.Verb);
        StringAssert.StartsWith(start.Arguments, "--mctier-helper BCCL-MCTier-test ");
        Assert.AreEqual(System.Diagnostics.ProcessWindowStyle.Hidden, start.WindowStyle);
    }
    [TestMethod]
    public void ReadsIndependentAesGcmV3Fixture()
    {
        // Produced independently using Node's crypto implementation and MCTier's documented envelope.
        const string secret = "mctier-invite-v3:AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKiuCW9UDG6hoakWY-mMnjnP_iKYdMQZEmDJGHTtgOAk6";
        var room = MCTierRoom.ParseInvite("mctier://join?v=3&name=Test&secret=" + secret);
        Assert.AreEqual("Passw0rd_中文", room.Password);
        Assert.AreEqual("Test", room.Name);
    }

    [TestMethod]
    public void UnicodeAndReservedCharactersRoundTrip()
    {
        var room = new MCTierRoom("中文 & + # 房间", "Password123&+=", "tcp://127.0.0.1:11010");
        Assert.AreEqual(room, MCTierRoom.ParseInvite(room.ToInvite()));
        Assert.AreNotEqual(room.ToInvite(), room.ToInvite());
    }

    [TestMethod]
    public void PasswordlessRoomAndLegacyLinksRemainCompatible()
    {
        var room = new MCTierRoom("test", "", MCTierRoom.DefaultNode);
        Assert.AreEqual(room, MCTierRoom.ParseInvite(room.ToInvite()));
        Assert.AreEqual("hello world", MCTierRoom.ParseInvite("mctier://join?v=2&name=test&pwd=hello+world").Password);
        Assert.AreEqual("12345678a", MCTierRoom.ParseInvite("test|12345678a").Password);
    }

    [TestMethod]
    public void ParsesFullInvitationAndMigratesOldNode()
    {
        var room = MCTierRoom.ParseInvite("邀请您加入大厅\n大厅名称：朋友的世界\n密码：Passw0rd\n服务器节点：tcp://mctier.pmhs.top:11010\n");
        Assert.AreEqual("朋友的世界", room.Name);
        Assert.AreEqual(MCTierRoom.DefaultNode, room.Node);
    }

    [TestMethod]
    public void RejectsTamperingAndDuplicateFields()
    {
        var invite = new MCTierRoom("test", "Passw0rd", MCTierRoom.DefaultNode).ToInvite();
        var position = invite.IndexOf("&node=", StringComparison.Ordinal);
        var tampered = invite[..(position - 5)] + (invite[position - 5] == 'A' ? "B" : "A") + invite[(position - 4)..];
        Assert.ThrowsExactly<ArgumentException>(() => MCTierRoom.ParseInvite(tampered));
        Assert.ThrowsExactly<ArgumentException>(() => MCTierRoom.ParseInvite("mctier://join?name=A&name=B"));
        Assert.ThrowsExactly<ArgumentException>(() => MCTierRoom.ParseInvite("mctier://join?v=99&name=A"));
    }

    [TestMethod]
    public void RejectsUnsafeNodesAndControlCharacters()
    {
        foreach (var node in new[] { "file:///C:/foo", "https://example.com", "tcp://x:0", "tcp://user:pass@host:12", "tcp://host:12 --foo" })
            Assert.ThrowsExactly<ArgumentException>(() => new MCTierRoom("test", "", node).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => new MCTierRoom("test\nname", "", MCTierRoom.DefaultNode).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => MCTierRoom.ParseInvite(new string('a', 12001)));
    }

    [TestMethod]
    public void ReadsNumericAndStringPeerMetrics()
    {
        var peers = MCTierEngine.ParsePeers("""
        [{"hostname":"Me","ipv4":"10.1.1.1/24","cost":"Local","lat_ms":0},
         {"hostname":"Friend","ipv4":"10.1.1.2","cost":"p2p","lat_ms":"12"},
         {"hostname":"Relay","ipv4":"","cost":"relay","lat_ms":"-"}]
        """);
        Assert.AreEqual(2, peers.Count);
        Assert.IsTrue(peers[0].IsLocal);
        Assert.AreEqual("直连", peers[1].Connection);
        Assert.AreEqual("12", peers[1].Latency);
    }
}
