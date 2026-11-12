using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BCCL.Core.Logging;

namespace BCCL.Core.Test;

[TestClass]
public class LoggerTest
{
    [TestMethod]
    public async Task DisposeFlushesFinalBatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BCCLLogFlush-" + System.Guid.NewGuid().ToString("N"));
        var logger = new Logger(new LoggerConfiguration(directory));
        logger.Info("last entry before exit");

        await logger.DisposeAsync();

        var logFiles = Directory.GetFiles(directory, "*.log");
        Assert.HasCount(1, logFiles);
        Assert.Contains("last entry before exit", File.ReadAllText(logFiles[0]));
        var resolved = Path.GetFullPath(directory);
        if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "BCCLLogFlush-"), System.StringComparison.OrdinalIgnoreCase))
            Directory.Delete(resolved, true);
    }

    [TestMethod]
    public void TestSimpleWrite()
    {
        var loggerOps = new LoggerConfiguration(
            Path.Combine(Path.GetTempPath(), "BCCLTest", "Logger"));
        var logger = new Logger(loggerOps);
        for (var i = 0; i < 10; i++)
            logger.Info($"Current we got {i}");
    }

    [TestMethod]
    public async Task TestHeavyWrite()
    {
        var loggerOps = new LoggerConfiguration(
            Path.Combine(Path.GetTempPath(), "BCCLTest", "Logger"));
        await using var logger = new Logger(loggerOps);
        var tasks = new List<Task>();
        for (var i = 0; i < 25; i++)
        {
            int current = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 25565; j++)
                {
                    logger.Info($"Current we got {current}:{j}");
                }
            }, TestContext.CancellationToken));
        }
        await Task.WhenAll(tasks.ToArray());
    }

    public TestContext TestContext { get; set; }
}
