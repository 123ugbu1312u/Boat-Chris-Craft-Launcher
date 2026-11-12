using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.IO.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.IO;

[TestClass]
public class ResourceProxyFallbackHandlerTest
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            token.ThrowIfCancellationRequested();
            return send(request, token);
        }
    }

    private static HttpRequestException Refused() =>
        new("Connection to 127.0.0.1 failed", new SocketException((int)SocketError.ConnectionRefused));

    [TestMethod]
    [DataRow("https://api.modrinth.com/v2/project/sodium/version")]
    [DataRow("https://cdn.modrinth.com/data/project/version/mod.jar")]
    [DataRow("https://api.curseforge.com/v1/mods/1/files")]
    [DataRow("https://edge.forgecdn.net/files/1/resource.zip")]
    [DataRow("https://mod.mcimirror.top/modrinth/v2/search")]
    public async Task RetriesPublicResourceAndPreservesRangeAndHeaders(string url)
    {
        var primary = new Handler((_, _) => Task.FromException<HttpResponseMessage>(Refused()));
        var direct = new Handler((request, _) =>
        {
            Assert.AreEqual("bytes=0-15", request.Headers.Range!.ToString());
            Assert.AreEqual("BCCL-Test/1.0", request.Headers.UserAgent.ToString());
            Assert.IsTrue(request.Options.TryGetValue(new HttpRequestOptionsKey<string>("test"), out var option));
            Assert.AreEqual("keep", option);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent([1, 2, 3]) });
        });
        using var fallback = new ResourceProxyFallbackHandler(direct, _ => true) { InnerHandler = primary };
        using var client = new HttpClient(fallback);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
        request.Headers.UserAgent.ParseAdd("BCCL-Test/1.0");
        request.Options.Set(new HttpRequestOptionsKey<string>("test"), "keep");
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(1, primary.Calls);
        Assert.AreEqual(1, direct.Calls);
    }

    [TestMethod]
    [DataRow("https://login.microsoftonline.com/token", true)]
    [DataRow("https://api.minecraftservices.com/minecraft/profile", true)]
    [DataRow("https://api.modrinth.com.attacker.test/v2/search", true)]
    [DataRow("http://api.modrinth.com/v2/search", true)]
    [DataRow("https://api.modrinth.com/v2/search", false)]
    public async Task LeavesLoginOtherHostsAndExplicitProxyPolicyAlone(string url, bool mayRetry)
    {
        var primary = new Handler((_, _) => Task.FromException<HttpResponseMessage>(Refused()));
        var direct = new Handler((_, _) => throw new AssertFailedException("Unexpected direct request"));
        using var fallback = new ResourceProxyFallbackHandler(direct, _ => mayRetry) { InnerHandler = primary };
        using var client = new HttpClient(fallback);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync(url));
        Assert.AreEqual(0, direct.Calls);
    }

    [TestMethod]
    public async Task DoesNotReplayPostsOrCertificateFailures()
    {
        var direct = new Handler((_, _) => throw new AssertFailedException("Unexpected direct request"));
        using (var fallback = new ResourceProxyFallbackHandler(direct, _ => true)
               { InnerHandler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(Refused())) })
        using (var client = new HttpClient(fallback))
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.PostAsync("https://api.modrinth.com/v2/search", new StringContent("{}")));

        var direct2 = new Handler((_, _) => throw new AssertFailedException("Unexpected direct request"));
        using var tlsFallback = new ResourceProxyFallbackHandler(direct2, _ => true)
        {
            InnerHandler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("TLS failed", new System.Security.Authentication.AuthenticationException())))
        };
        using var tlsClient = new HttpClient(tlsFallback);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => tlsClient.GetAsync("https://api.modrinth.com/v2/search"));
        Assert.AreEqual(0, direct.Calls + direct2.Calls);
    }

    [TestMethod]
    public async Task KeepsSuccessfulProxyResponseAndCancellation()
    {
        var direct = new Handler((_, _) => throw new AssertFailedException("Unexpected direct request"));
        using var fallback = new ResourceProxyFallbackHandler(direct, _ => true)
        {
            InnerHandler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))
        };
        using var client = new HttpClient(fallback);
        using var response = await client.GetAsync("https://api.modrinth.com/v2/search");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetAsync("https://api.modrinth.com/v2/search", cancelled.Token));
        Assert.AreEqual(0, direct.Calls);
    }
}
