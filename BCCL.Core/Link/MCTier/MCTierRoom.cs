// 基于 MCTier 项目开发。原作者：MCTier Team。
// https://github.com/pmh1314520/MCTier — 原许可见 Codes/Licenses/MCTier/LICENSE。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BCCL.Core.Link.MCTier;

/// <summary>MCTier's wire identity is the unprefixed room name and password.</summary>
public sealed record MCTierRoom(string Name, string Password, string Node)
{
    public const string DefaultNode = "tcp://easytier.weiai.org.cn:11010";
    private const string Prefix = "mctier-invite-v3:";
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("MCTier/invite/v3");

    // Keep existing credentials unchanged. Adapt values the directory/environment cannot carry,
    // so user passwords have no composition/length rules and are never silently truncated.
    public string NetworkPassword => Encoding.UTF8.GetByteCount(Password) <= 256 && !Password.Any(char.IsControl)
        ? Password : "bccl-sha256-v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password)));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64 || Name.Any(char.IsControl))
            throw new ArgumentException("大厅名称应为 1–64 个字符，不能包含换行或控制字符。");
        ValidateNode(Node);
    }

    public static string ValidateNode(string node)
    {
        if (node.Length > 256 || node.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(node, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("tcp" or "udp" or "ws" or "wss") ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            ((uri.Scheme is "tcp" or "udp") && uri.Port is < 1 or > 65535))
            throw new ArgumentException("节点地址无效，请填写 tcp://主机:端口、udp://主机:端口 或 ws(s):// 地址。");
        return node;
    }

    public string ToInvite()
    {
        Validate();
        var secret = "";
        if (Password.Length > 0)
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plain = Encoding.UTF8.GetBytes(NetworkPassword);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Aad);
            secret = Prefix + Convert.ToBase64String(key.Concat(nonce).Concat(cipher).Concat(tag).ToArray())
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        return $"mctier://join?v=3&name={Uri.EscapeDataString(Name)}&secret={Uri.EscapeDataString(secret)}&node={Uri.EscapeDataString(Node)}";
    }

    public static MCTierRoom ParseInvite(string text)
    {
        if (text.Length > 12000) throw new ArgumentException("邀请内容过长。");
        var match = Regex.Match(text.Trim(), @"mctier://join/?\?[^\s]+", RegexOptions.IgnoreCase);
        string name, password, node;
        if (match.Success)
        {
            var uri = new Uri(match.Value);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = part.Split('=', 2);
                if (!fields.TryAdd(WebUtility.UrlDecode(pair[0]), pair.Length == 2 ? WebUtility.UrlDecode(pair[1]) : ""))
                    throw new ArgumentException("邀请链接包含重复字段。");
            }
            var version = fields.GetValueOrDefault("v", "1");
            if (version is not ("1" or "2" or "3")) throw new ArgumentException("暂不支持此邀请版本。");
            name = fields.GetValueOrDefault("name", "");
            password = version == "3" ? OpenSecret(fields.GetValueOrDefault("secret", "")) : fields.GetValueOrDefault("pwd", "");
            node = fields.GetValueOrDefault("node", DefaultNode);
        }
        else
        {
            string Field(string labels) => Regex.Match(text, $@"(?:{labels})\s*[:：]\s*([^\r\n]*)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            name = Field("大厅名称|Lobby Name");
            password = Field("密码|Password");
            node = Field("服务器节点|Server Node");
            if (node.Length == 0) node = DefaultNode;
            if (name.Length == 0)
            {
                var parts = text.Trim().Split('|');
                if (parts.Length != 2) throw new ArgumentException("未识别到 MCTier 邀请，请粘贴完整链接或邀请文本。");
                name = parts[0].Trim();
                password = parts[1].Trim();
            }
        }
        if (node is "tcp://mctier.pmhs.top:11010" or "udp://mctier.pmhs.top:11010" or "ws://test.pmhs.top" or "wss://test.pmhs.top") node = DefaultNode;
        var room = new MCTierRoom(name, password, node);
        room.Validate();
        return room;
    }

    private static string OpenSecret(string value)
    {
        if (value.Length == 0) return "";
        if (value.Length > 4096 || !value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new ArgumentException("邀请密钥格式无效。");
        try
        {
            var encoded = value[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
            if (bytes.Length < 60) throw new FormatException();
            var plain = new byte[bytes.Length - 60];
            using var aes = new AesGcm(bytes.AsSpan(0, 32), 16);
            aes.Decrypt(bytes.AsSpan(32, 12), bytes.AsSpan(44, plain.Length), bytes.AsSpan(bytes.Length - 16), plain, Aad);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or DecoderFallbackException)
        { throw new ArgumentException("邀请密钥已损坏，请重新复制完整邀请。", ex); }
    }
}
