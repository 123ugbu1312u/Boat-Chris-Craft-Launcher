using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BCCL.Core.Minecraft;

[TestClass]
public class MavenTest
{
    [TestMethod]
    public void ParsePathAndUri()
    {
        string[] mavenId = ["io.github.copytiao:bccl:0.6.2","io.github.copytiao:bccl:jar:0.6.2",
        "io.github.copytiao:bccl:snapshot:0.6.2",
        "io.github.copytiao:bccl:jar:snapshot:0.6.2"];
        foreach(var id in mavenId)
        {
            var maven = new MavenArtifact(id);
            Console.WriteLine($"Uri: {maven.Resolve("https://copytiao.github.io/maven/copytiao/")}");
            Console.WriteLine($"Path: {maven.Resolve("C:/Users/copytiao/AppData/Roaming/.minecraft/library/maven")}");
        }
    }
    [TestMethod]
    public void ParseInvalidId()
    {
        string[] badIds = [
            "io",
            "io.github.copytiao:luotianyi:yuezhengling:xinchen:bccl:tests:aaa"
        ];
        foreach(var id in badIds)
        {
            try
            {
                var package = new MavenArtifact(id);
                package.Resolve("");
            }catch(FormatException ex)
            {
                Console.WriteLine($"Debug Output: {Environment.NewLine}{ex}");
            }
        }
    }
}