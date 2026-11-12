using System;
using System.IO;
using BCCL.Core.App.Essentials;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.App;

[TestClass]
public sealed class UpdateHelperRegressionTest
{
    [TestMethod]
    public void ReplaceUpdatesTargetAndKeepsSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BCCL-update-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "old.exe");
            var source = Path.Combine(directory, "new.exe");
            File.WriteAllText(target, "old");
            File.WriteAllText(source, "new");
            Assert.IsNull(UpdateHelper.Replace(source, target));
            Assert.AreEqual("new", File.ReadAllText(target));
            Assert.AreEqual("new", File.ReadAllText(source));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void MissingSourceDoesNotDeleteOldTarget()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BCCL-update-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "old.exe");
            File.WriteAllText(target, "old");
            Assert.IsNotNull(UpdateHelper.Replace(Path.Combine(directory, "missing.exe"), target));
            Assert.AreEqual("old", File.ReadAllText(target));
        }
        finally { Directory.Delete(directory, true); }
    }
}
