using System.Text.Json;
using BCCL.Core.Utils;

namespace BCCL;

/// <summary>Resource sharing uses the same JSON ID list format independently of bookmarks.</summary>
internal static class CompShare
{
    public static string GetShareCode(HashSet<string> ids)
    {
        try { return JsonSerializer.Serialize(ids, JsonCompat.SerializerOptions); }
        catch (Exception ex) { ModBase.Log(ex, "生成资源分享内容出错"); return ""; }
    }
}
