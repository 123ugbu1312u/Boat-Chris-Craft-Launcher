using BCCL.Core.UI.Theme;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace BCCL.Core.Test;
[TestClass]
public class GlassQualityPolicyTest
{
    [TestMethod] public void CpuPressureReducesQualityPromptly() => Assert.AreEqual(40, GlassQualityPolicy.Next(60, 95, 10));
    [TestMethod] public void GpuPressureAlsoReducesQuality() => Assert.AreEqual(40, GlassQualityPolicy.Next(60, 10, 95));
    [TestMethod] public void IdleRecoveryIsGradual() => Assert.AreEqual(30, GlassQualityPolicy.Next(25, 10, 10));
    [TestMethod] public void MissingTelemetryNeverEnablesFullQuality() { var q=80; for(var i=0;i<30;i++) q=GlassQualityPolicy.Next(q, 10, null); Assert.IsTrue(q<=35); }
    [TestMethod] public void SustainedLoadConvergesWithoutOscillation() { var q=85; for(var i=0;i<30;i++) q=GlassQualityPolicy.Next(q, 99, 99); Assert.AreEqual(10,q); Assert.AreEqual(q,GlassQualityPolicy.Next(q,99,99)); }
    [TestMethod] public void InvalidInputsStayBounded() { Assert.IsTrue(GlassQualityPolicy.Next(-20, 100, 100)>=0); Assert.IsTrue(GlassQualityPolicy.Next(200,0,0)<=100); }
}
