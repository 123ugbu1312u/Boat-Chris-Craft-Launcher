using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using BCCL.Core.App;

// Isolated adapters only: avoid launching WPF, loading accounts or changing user configuration.
// File downloading, source probing, Range recovery, quotas and throttling compile directly from production sources.
namespace BCCL
{
    public static class ModBase
    {
        public enum LogLevel { Debug }
        public static void Log(string message) => Console.WriteLine(message);
        public static void Log(Exception? ex, string message, LogLevel level) => Console.WriteLine(message + ": " + ex?.Message);
        public sealed class FileChecker(long actualSize = -1, string? hash = null)
        {
            public readonly long actualSize = actualSize;
            public readonly string? hash = hash;
            public string? Check(string path)
            {
                if (!File.Exists(path)) return "Missing file";
                if (actualSize >= 0 && new FileInfo(path).Length != actualSize) return "Size mismatch";
                if (hash is not null)
                {
                    using var stream = File.OpenRead(path);
                    if (!Convert.ToHexString(SHA1.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return "SHA1 mismatch";
                }
                return null;
            }
        }
    }
}

namespace BCCL.Network
{
    internal static class Config
    {
        internal static class Download
        {
            internal static int FileSource = 1;
            internal static DownloadHttpMode HttpMode = DownloadHttpMode.Auto;
        }
    }
    internal static class NetworkService
    {
        public const string CurseForgeApi = "curseforge";
        public static HttpClient Client = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
        public static HttpClient GetClient(string name = "") => Client;
    }
    internal static class RequestSigning
    {
        public static string SecretCdnSign(string value) => value;
        public static void SecretHeadersSign(string url, ref HttpRequestMessage request, bool browser = false, string custom = "")
            => request.Headers.UserAgent.ParseAdd("BCCL-DownloadVerification/1.0");
    }
    public enum NetState { WaitingToCheck, Connecting, Downloading, Finished }
    public sealed class DownloadFile
    {
        public ModBase.FileChecker? Check { get; set; }
        public NetState State { get; set; }
        public long TotalSize { get; set; }
        public bool IsUnknownSize { get; set; }
        public long DownloadedBytes { get; set; }
        public long Speed { get; set; }
        public int ActiveThreads { get; set; }
    }
    internal static class ModNet
    {
        public const string NetDownloadEnd = ".BCCLDownloading";
        public const int NetTaskConnectionLimitMax = 256;
        public const int NetTaskConnectionsPerHostLimit = 256;
        public const int NetTaskSingleFileConnectionLimitMax = 8;
        public const long NetTaskBufferBudgetBytes = 512L * 1024 * 1024;
        public static int NetTaskConnectionLimit = 64;
        public static int NetTaskSingleFileConnectionLimit = 8;
        public static long NetTaskSpeedLimitHigh = -1;
    }
}
