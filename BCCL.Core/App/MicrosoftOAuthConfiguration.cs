using System;
using System.IO;
using System.Text.Json;

namespace BCCL.Core.App;

/// <summary>Stores only the public application ID, never account credentials.</summary>
public static class MicrosoftOAuthConfiguration
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "BCCL", "MicrosoftOAuth.json");

    public static string Normalize(string? value)
    {
        if (!Guid.TryParse(value?.Trim(), out var id) || id == Guid.Empty)
            throw new ArgumentException("Microsoft Client ID 必须是非空的应用程序 GUID。");
        return id.ToString("D");
    }

    public static string Read(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return Normalize(json.RootElement.GetProperty("clientId").GetString());
    }

    public static void Save(string path, string clientId)
    {
        var normalized = Normalize(clientId);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { clientId = normalized }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
