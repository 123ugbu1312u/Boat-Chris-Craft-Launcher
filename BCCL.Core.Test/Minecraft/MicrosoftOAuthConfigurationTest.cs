using System;
using System.Linq;
using System.Reflection;
using BCCL.Core.App;
using BCCL.Core.Minecraft.IdentityModel;
using BCCL.Core.Minecraft.Profile.Authentication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Minecraft;

[TestClass]
[DoNotParallelize]
public sealed class MicrosoftOAuthConfigurationTest
{
    // Synthetic identifiers for configuration tests; never sent to Microsoft.
    private const string RuntimeId = "260c9f33-c462-4dcc-b49f-fc495adad16e";

    [TestMethod]
    public void ReleaseBuildCanReadRuntimePublicClientId()
    {
        WithRuntimeId($"  {RuntimeId}  ", () =>
        {
            Assert.AreEqual(RuntimeId, Secrets.MSOAuthClientId);
            _ = new MicrosoftProvider();
        });
    }

    [TestMethod]
    public void EmptyRuntimeSettingUsesEmbeddedApplicationId()
    {
        WithRuntimeId(" ", () =>
        {
            var embedded = typeof(Secrets).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "BCCL.MicrosoftOAuthClientId")?.Value ?? "";
            Assert.AreEqual(embedded, Secrets.MSOAuthClientId);
            if (embedded.Length == 0)
                Assert.ThrowsExactly<IdentityModelConfigurationException>(() => new MicrosoftProvider());
            else
                _ = new MicrosoftProvider();
        });
    }

    [TestMethod]
    public void ExplicitProviderConfigurationWorksWithoutRuntimeSetting()
    {
        WithRuntimeId(null, () => _ = new MicrosoftProvider(RuntimeId));
    }

    private static void WithRuntimeId(string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable("BCCL_MS_CLIENT_ID");
        try
        {
            Environment.SetEnvironmentVariable("BCCL_MS_CLIENT_ID", value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("BCCL_MS_CLIENT_ID", previous);
        }
    }
}
