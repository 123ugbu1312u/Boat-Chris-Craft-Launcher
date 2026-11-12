using System;
using System.Collections.Concurrent;
using System.IO;

namespace BCCL.Core.App;

/// <summary>Immutable identifiers used only when importing data from earlier launcher versions.</summary>
public static class LegacyCompatibility
{
    public const string LocalDirectoryName = "\u0050CL";
    public const string SharedDirectoryName = "\u0050CLCE";
    public const string DisplayName = "\u0050CL CE";
    public const string SecondName = "\u0050CL2";
    public const string UpdateResourceId = "\u0050CL2-CE";
    private const string EarlierAssemblyName = "Plain Craft Launcher 2";

    // These bytes are part of the existing encrypted key-file format, not branding.
    public static byte[] KeyEntropy => [80, 67, 76, 32, 67, 69, 32, 69, 110, 99, 114, 121, 112, 116, 105, 111, 110, 32, 75, 101, 121];

    private static readonly object MigrationLock = new();
    private static readonly ConcurrentDictionary<string, bool> ImportedInstances = new(StringComparer.OrdinalIgnoreCase);

    public static void CopySharedData(string legacyName, string sharedData, string sharedLocalData)
    {
        lock (MigrationLock)
        {
            DataDirectoryMigration.CopyLegacyData(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), legacyName), sharedData);
            DataDirectoryMigration.CopyLegacyData(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), legacyName), sharedLocalData);
        }
    }

    public static void EnsureInstanceData(string instanceDirectory)
    {
        var key = Path.GetFullPath(instanceDirectory);
        lock (MigrationLock)
        {
            if (ImportedInstances.ContainsKey(key)) return;
            DataDirectoryMigration.CopyLegacyData(Path.Combine(key, LocalDirectoryName),
                Path.Combine(key, Paths.DataDirectoryName));
            ImportedInstances[key] = true;
        }
    }

    public static void EnsureGameFolderIndex(string fileName)
    {
        if (!Path.GetFileName(fileName).Equals("BCCL.ini", StringComparison.OrdinalIgnoreCase)) return;
        lock (MigrationLock)
        {
            if (File.Exists(fileName)) return;
            var parent = Path.GetDirectoryName(fileName);
            if (parent is null) return;
            var earlier = Path.Combine(parent, LocalDirectoryName + ".ini");
            if (File.Exists(earlier)) File.Copy(earlier, fileName);
        }
    }

    public static string RewriteXaml(string xaml) => xaml
        .Replace("clr-namespace:" + LocalDirectoryName, "clr-namespace:BCCL", StringComparison.Ordinal)
        .Replace("assembly=" + LocalDirectoryName + ".Core", "assembly=BCCL.Core", StringComparison.Ordinal)
        .Replace("assembly=" + EarlierAssemblyName, "assembly=BCCL", StringComparison.Ordinal)
        .Replace("/" + EarlierAssemblyName + ";component/", "/BCCL;component/", StringComparison.Ordinal)
        .Replace("/" + LocalDirectoryName + ".Core;component/", "/BCCL.Core;component/", StringComparison.Ordinal);
}
