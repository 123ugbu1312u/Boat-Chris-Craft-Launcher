using System.Text;

namespace BCCL.Network;

public struct RequestParam
{
    public Encoding? Encoding { get; set; }
    public string? Accept { get; set; }
    public string? FallbackUrl { get; set; }
    public bool UseBrowserUserAgent { get; set; }
    public int Timeout { get; set; }
    public int Retries { get; set; }
    public CancellationToken CancellationToken { get; set; }

    public static RequestParam WithRetry => new()
    {
        Timeout = 30000,
        Retries = 3
    };
}
