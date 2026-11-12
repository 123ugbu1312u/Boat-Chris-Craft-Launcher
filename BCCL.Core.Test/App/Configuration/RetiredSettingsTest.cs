using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BCCL.Core.App;
using BCCL.Core.App.Configuration;
using BCCL.Core.App.Configuration.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.App.Configuration;

[TestClass]
public sealed class RetiredSettingsTest
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void OldOptionIsRemovedWhileOtherSettingsSurvive(bool yaml, bool enabled)
    {
        var folder = Path.Combine(Path.GetTempPath(), "BCCL-retired-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, yaml ? "config.yml" : "config.json");
        try
        {
            IKeyValueFileProvider Open() => yaml ? new YamlFileProvider(path) : new JsonFileProvider(path);
            var provider = Open();
            provider.Set("SystemTelemetry", enabled);
            provider.Set("SystemMaxLog", 19);
            provider.Set("FutureSetting", "preserve me");
            provider.Sync();

            Assert.IsTrue(ConfigMigration.RemoveRetiredSettings(Open()));
            var cleaned = Open();
            Assert.IsFalse(cleaned.Exists("SystemTelemetry"));
            Assert.AreEqual(19, cleaned.Get<int>("SystemMaxLog"));
            Assert.AreEqual("preserve me", cleaned.Get<string>("FutureSetting"));
            var contents = File.ReadAllBytes(path);
            Assert.IsFalse(ConfigMigration.RemoveRetiredSettings(cleaned));
            CollectionAssert.AreEqual(contents, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void CoreHasNoReportingServiceSdkOrConfigProperty()
    {
        var core = typeof(Config).Assembly;
        Assert.IsNull(core.GetType("BCCL.Core.App.Essentials.TelemetryService"));
        Assert.IsFalse(core.GetReferencedAssemblies().Any(reference =>
            reference.Name is "Sentry" or "Stun.Net"));
        var properties = typeof(Config).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(group => group.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance | BindingFlags.Static));
        Assert.IsFalse(properties.Any(property => property.Name is "Telemetry" or "TelemetryConfig"));
    }
}
