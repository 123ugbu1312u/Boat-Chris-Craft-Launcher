using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BCCL.Core.App;

namespace BCCL.Core.Test.App;

[TestClass]
public class DataDirectoryMigrationTest
{
    private string _root = null!;
    private string Legacy => Path.Combine(_root, LegacyCompatibility.LocalDirectoryName);
    private string Destination => Path.Combine(_root, "BCCL");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "BCCLDataTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var resolved = Path.GetFullPath(_root);
        var expectedPrefix = Path.Combine(Path.GetTempPath(), "BCCLDataTests-");
        if (!resolved.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete outside the test directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MovePreservesFilesAndRemovesOldDirectory(bool createEmptyDestination)
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "Log"));
        File.WriteAllText(Path.Combine(Legacy, "config.v1.yml"), "LaunchArgumentInfo: BCCL");
        var content = new byte[] { 0, 1, 255, 42 };
        File.WriteAllBytes(Path.Combine(Legacy, "Log", "old.log"), content);
        if (createEmptyDestination) Directory.CreateDirectory(Destination);

        DataDirectoryMigration.Migrate(Legacy, Destination);

        Assert.IsFalse(Directory.Exists(Legacy));
        Assert.AreEqual("LaunchArgumentInfo: BCCL", File.ReadAllText(Path.Combine(Destination, "config.v1.yml")));
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(Destination, "Log", "old.log")));
    }

    [TestMethod]
    public void ExistingDestinationKeepsBothSetsAndDoesNotRepeat()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "Musics"));
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Legacy, "config.v1.yml"), "old config");
        File.WriteAllText(Path.Combine(Destination, "config.v1.yml"), "new config");
        File.WriteAllText(Path.Combine(Legacy, "Musics", "track.ogg"), "music data");

        DataDirectoryMigration.Migrate(Legacy, Destination);
        DataDirectoryMigration.Migrate(Legacy, Destination);

        Assert.IsFalse(Directory.Exists(Legacy));
        Assert.AreEqual("new config", File.ReadAllText(Path.Combine(Destination, "config.v1.yml")));
        Assert.AreEqual("music data", File.ReadAllText(Path.Combine(Destination, "Musics", "track.ogg")));
        var backups = Directory.GetDirectories(Destination, "LegacyData-*");
        Assert.HasCount(1, backups);
        Assert.AreEqual("old config", File.ReadAllText(Path.Combine(backups[0], "config.v1.yml")));
    }

    [TestMethod]
    public void AbsentLegacyDoesNotCreateAnOldFolderOrAlterNewData()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "config.v1.yml"), "existing config");

        DataDirectoryMigration.Migrate(Legacy, Destination);

        Assert.IsFalse(Directory.Exists(Legacy));
        Assert.AreEqual("existing config", File.ReadAllText(Path.Combine(Destination, "config.v1.yml")));
    }

    [TestMethod]
    public void RemapChangesOnlyPathsInsideTheLegacyDirectory()
    {
        Assert.AreEqual(Path.Combine(Destination, "MyDownload"),
            DataDirectoryMigration.RemapPath(Path.Combine(Legacy, "MyDownload"), Legacy, Destination));
        Assert.AreEqual(Destination, DataDirectoryMigration.RemapPath(Legacy, Legacy, Destination));
        var unrelated = Path.Combine(_root, "BCCL-other", "MyDownload");
        Assert.AreEqual(unrelated, DataDirectoryMigration.RemapPath(unrelated, Legacy, Destination));
        Assert.AreEqual("", DataDirectoryMigration.RemapPath("", Legacy, Destination));
        Assert.AreEqual("relative", DataDirectoryMigration.RemapPath("relative", Legacy, Destination));
    }

    [TestMethod]
    public void RejectsNestedTargetsWithoutMovingData()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "config.v1.yml"), "keep");

        Assert.ThrowsExactly<ArgumentException>(() =>
            DataDirectoryMigration.Migrate(Legacy, Path.Combine(Legacy, "BCCL")));

        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(Legacy, "config.v1.yml")));
    }
}
