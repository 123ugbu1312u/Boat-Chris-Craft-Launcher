using System;
using System.IO;
using System.Linq;

namespace BCCL.Core.App;

/// <summary>Moves launcher-local data to its new directory without overwriting existing data.</summary>
public static class DataDirectoryMigration
{
    public static void Migrate(string legacyDirectory, string dataDirectory)
    {
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        if (string.Equals(legacy, destination, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(legacy), Path.GetDirectoryName(destination),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Data migration requires two distinct sibling directories.");

        if (!Directory.Exists(legacy)) return;
        if (!Directory.Exists(destination))
        {
            Directory.Move(legacy, destination);
            return;
        }

        // Paths creates an empty destination during single-instance lock setup.
        if (!Directory.EnumerateFileSystemEntries(destination).Any())
        {
            Directory.Delete(destination); // Empty directory only, never recursive.
            Directory.Move(legacy, destination);
            return;
        }

        // Both folders already contain data: keep the new configuration and
        // retain every legacy file in a backup inside the new data directory.
        var backup = Path.Combine(destination, "LegacyData-" + Guid.NewGuid().ToString("N"));
        Directory.Move(legacy, backup);
        CopyMissingFiles(backup, destination);
    }

    private static void CopyMissingFiles(string source, string destination)
    {
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            // Preserve links in the backup without following them outside the data tree.
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo)
            {
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(target);
                CopyMissingFiles(entry.FullName, target);
            }
            else if (!File.Exists(target) && !Directory.Exists(target))
            {
                File.Copy(entry.FullName, target);
            }
        }
    }

    public static void CopyLegacyData(string legacyDirectory, string dataDirectory)
    {
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        if (string.Equals(legacy, destination, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(Path.GetDirectoryName(legacy), Path.GetDirectoryName(destination),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Data import requires sibling directories.");
        if (!Directory.Exists(legacy)) return;
        Directory.CreateDirectory(destination);
        CopyMissingFiles(legacy, destination);
    }

    public static string RemapPath(string value, string legacyDirectory, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) return value;
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        var path = Path.GetFullPath(value);
        if (string.Equals(Path.TrimEndingDirectorySeparator(path), legacy, StringComparison.OrdinalIgnoreCase))
            return dataDirectory;
        var prefix = legacy + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(dataDirectory, path[prefix.Length..])
            : value;
    }
}
