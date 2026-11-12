using System;
using System.Linq;
using System.Reflection;
using BCCL.Core.Utils.Exts;
using BCCL.Core.Utils.OS;

namespace BCCL.Core.App;

// ReSharper disable InconsistentNaming
public static class Secrets
{
    /// <summary>
    /// 微软 OAuth 的 Client ID
    /// </summary>
    public static string MSOAuthClientId
    {
        get
        {
            // A public OAuth application identifier may be supplied to Release builds too.
            var runtimeId = Environment.GetEnvironmentVariable("BCCL_MS_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(runtimeId)) return runtimeId.Trim();
            var localId = MicrosoftOAuthConfiguration.Read(MicrosoftOAuthConfiguration.FilePath);
            if (!string.IsNullOrWhiteSpace(localId)) return localId;
            if (!string.IsNullOrWhiteSpace(EmbeddedMSOAuthClientId)) return EmbeddedMSOAuthClientId.Trim();
            return EnvironmentInterop.GetSecret("MS_CLIENT_ID", readEnv: false)?.Trim() ?? string.Empty;
        }
    }

    private static readonly string? EmbeddedMSOAuthClientId = typeof(Secrets).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "BCCL.MicrosoftOAuthClientId")?.Value;

    /// <summary>
    /// CurseForge API 的 Client ID
    /// </summary>
    public static string CurseForgeAPIKey { get; } = EnvironmentInterop.GetSecret("CURSEFORGE_API_KEY", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// Natayark ID OAuth 的 Client ID
    /// </summary>
    public static string NatayarkClientId { get; } = EnvironmentInterop.GetSecret("NAID_CLIENT_ID", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// Natayark ID OAuth 的 Client ID
    /// </summary>
    public static string NatayarkClientSecret { get; } = EnvironmentInterop.GetSecret("NAID_CLIENT_SECRET", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 联机根服务器
    /// </summary>
    public static string[] LinkServers { get; } = EnvironmentInterop.GetSecret("LINK_SERVER_ROOT", readEnvDebugOnly: true).ReplaceNullOrEmpty().Split("|");

    /// <summary>
    /// 当前版本的 Git 提交 SHA
    /// </summary>
    public static string CommitHash { get; } = EnvironmentInterop.GetSecret("GITHUB_SHA", readEnvDebugOnly: true).ReplaceNullOrEmpty();
}
