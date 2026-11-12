using System;
using System.IO;
using System.Text;
using BCCL.Core.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.App;

[TestClass]
public class LegacyCompatibilityTest
{
    [TestMethod]
    public void ExistingEncryptedKeyEntropyIsUnchanged()
    {
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("\u0050CL CE Encryption Key"), LegacyCompatibility.KeyEntropy);
    }

    [TestMethod]
    public void EarlierCustomHomepageCanResolveRenamedAssemblies()
    {
        var earlier = "<local:MyCard xmlns:local=\"clr-namespace:\u0050CL;assembly=Plain Craft Launcher 2\" " +
                      "xmlns:core=\"clr-namespace:\u0050CL.Core.UI;assembly=\u0050CL.Core\" " +
                      "Tag=\"/Plain Craft Launcher 2;component/Images/icon.ico\" />";

        var rewritten = LegacyCompatibility.RewriteXaml(earlier);

        StringAssert.Contains(rewritten, "clr-namespace:BCCL;assembly=BCCL");
        StringAssert.Contains(rewritten, "clr-namespace:BCCL.Core.UI;assembly=BCCL.Core");
        StringAssert.Contains(rewritten, "/BCCL;component/Images/icon.ico");
    }

    [TestMethod]
    public void LegacyImportPreservesOriginalAndExistingDestinationFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "BCCLImportTest-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Earlier");
        var destination = Path.Combine(root, "BCCL");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            File.WriteAllBytes(Path.Combine(source, "UserKey.bin"), [1, 2, 3, 4]);
            File.WriteAllText(Path.Combine(source, "config.v1.json"), "earlier profiles");
            File.WriteAllText(Path.Combine(destination, "config.v1.json"), "current profiles");

            DataDirectoryMigration.CopyLegacyData(source, destination);

            Assert.AreEqual("current profiles", File.ReadAllText(Path.Combine(destination, "config.v1.json")));
            Assert.AreEqual("earlier profiles", File.ReadAllText(Path.Combine(source, "config.v1.json")));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(destination, "UserKey.bin")));
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "BCCLImportTest-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(resolved, true);
        }
    }
}
