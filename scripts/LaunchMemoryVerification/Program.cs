using System;
using System.IO;
using System.Reflection;
using BCCL.Core.App;
using BCCL.Core.App.Configuration;
using BCCL.Core.App.Configuration.Storage;

namespace BCCL.LaunchMemoryVerification;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Initialize only the generated registry. Never open the user's configuration providers.
        var service = typeof(ConfigService);
        var initialized = service.GetField("_isConfigItemsInitialized", BindingFlags.Static | BindingFlags.NonPublic)!;
        if (!(bool)initialized.GetValue(null)!)
        {
            service.GetMethod("_InitializeConfigItems", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            initialized.SetValue(null, true);
        }
        var root = Path.Combine(Path.GetTempPath(), "BCCL-launch-memory-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var localPath = Path.Combine(root, "launcher.yml");
        var instanceA = Path.Combine(root, "a");
        var instanceB = Path.Combine(root, "b");
        var local = new FileConfigStorage(new YamlFileProvider(localPath));
        DynamicCacheConfigStorage OpenInstances() => new()
        {
            StorageFactory = argument => new FileConfigStorage(new YamlFileProvider(argument + ".yml"))
        };
        var instances = OpenInstances();
        var globalItem = Config.Launch.OptimizeMemoryBeforeLaunchConfig;
        var instanceItem = Config.Instance.MemoryOptimizationModeConfig;
        var allocationItem = Config.Instance.MemorySolutionConfig;
        void Bind()
        {
            BindProvider(globalItem, local);
            BindProvider(instanceItem, instances);
            BindProvider(allocationItem, instances);
        }
        try
        {
            Bind();
            CheckFalse(Config.IsLaunchMemoryOptimizationEnabled(instanceA), "Disabled by default.");
            ConfigService.TrySetValue("LaunchRamOptimize", true);
            CheckTrue(Config.IsLaunchMemoryOptimizationEnabled(instanceA), "Default instance follows global.");
            ConfigService.TrySetValue("VersionRamOptimize", 2, instanceA);
            CheckFalse(Config.IsLaunchMemoryOptimizationEnabled(instanceA), "Instance can opt out.");
            CheckTrue(Config.IsLaunchMemoryOptimizationEnabled(instanceB), "Other instances remain independent.");
            ConfigService.TrySetValue("LaunchRamOptimize", false);
            ConfigService.TrySetValue("VersionRamOptimize", 1, instanceA);
            for (var allocation = 0; allocation <= 2; allocation++)
            {
                ConfigService.TrySetValue("VersionRamType", allocation, instanceA);
                CheckTrue(Config.IsLaunchMemoryOptimizationEnabled(instanceA), "Optimization is independent of heap allocation mode.");
            }
            local.Stop();
            instances.Stop();
            local = new FileConfigStorage(new YamlFileProvider(localPath));
            instances = OpenInstances();
            Bind();
            CheckFalse(Config.Launch.OptimizeMemoryBeforeLaunch);
            CheckTrue(Config.IsLaunchMemoryOptimizationEnabled(instanceA), "Override survives reopening YAML.");
            CheckFalse(Config.IsLaunchMemoryOptimizationEnabled(instanceB));
            Config.Instance.MemoryOptimizationModeConfig.Reset(instanceA);
            // Reset deletes are queued by FileConfigStorage; flush before testing a cold reload.
            instances.Stop();
            instances = OpenInstances();
            Bind();
            CheckFalse(Config.IsLaunchMemoryOptimizationEnabled(instanceA), "Reset returns to global.");
            Config.Launch.OptimizeMemoryBeforeLaunch = true;
            CheckTrue(Config.IsLaunchMemoryOptimizationEnabled(instanceA));
        }
        finally
        {
            local.Stop();
            instances.Stop();
            BindProvider(globalItem, null);
            BindProvider(instanceItem, null);
            BindProvider(allocationItem, null);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckTrue(bool value, string message = "Configuration check") { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
    private static void CheckFalse(bool value, string message = "Configuration check") => CheckTrue(!value, message);

    private static void BindProvider<T>(ConfigItem<T> item, IConfigProvider? provider)
    {
        item.EnableCache = false;
        typeof(ConfigItem<T>).GetField("<_Provider>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, provider);
        item.EnableCache = true;
    }
}

