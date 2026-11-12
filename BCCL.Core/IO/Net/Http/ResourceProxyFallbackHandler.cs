using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.Logging;

namespace BCCL.Core.IO.Net.Http;

/// <summary>Recover public resource GETs when a stale system loopback proxy refuses connections.</summary>
public sealed class ResourceProxyFallbackHandler : DelegatingHandler
{
    private readonly HttpMessageInvoker _direct;
    private readonly Func<Uri, bool> _canRetryDirectly;

    public ResourceProxyFallbackHandler() : this(new SocketsHttpHandler
    {
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 20,
        ConnectTimeout = TimeSpan.FromSeconds(15)
        // Use normal system DNS for this recovery path; DoH may use the same dead proxy.
    }, HttpProxyManager.Instance.CanRetryResourceDirectly) { }

    public ResourceProxyFallbackHandler(HttpMessageHandler directHandler, Func<Uri, bool> canRetryDirectly)
    {
        _direct = new HttpMessageInvoker(directHandler);
        _canRetryDirectly = canRetryDirectly;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested &&
            request.RequestUri is { } destination && request.Content is null &&
            (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) &&
            IsPublicResource(destination) && _canRetryDirectly(destination) && IsConnectionRefused(ex))
        {
            LogWrapper.Warn("Proxy", $"系统本地代理拒绝连接，公开资源请求尝试直连：{request.RequestUri!.Host}");
            using var retry = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };
            foreach (var header in request.Headers)
                retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var option in request.Options)
                retry.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            return await _direct.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsPublicResource(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.IdnHost;
        return host.Equals("api.curseforge.com", StringComparison.OrdinalIgnoreCase) ||
               IsDomain(host, "modrinth.com") || IsDomain(host, "forgecdn.net") || IsDomain(host, "mcimirror.top");
    }

    private static bool IsDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectionRefused(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) return true;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _direct.Dispose();
        base.Dispose(disposing);
    }
}
