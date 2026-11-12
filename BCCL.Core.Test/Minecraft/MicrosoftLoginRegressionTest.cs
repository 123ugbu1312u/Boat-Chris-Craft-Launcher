using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.App;
using BCCL.Core.IO.Net.Http;
using BCCL.Core.Minecraft.IdentityModel;
using BCCL.Core.Minecraft.IdentityModel.OAuth;
using BCCL.Core.Minecraft.Profile.Authentication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Minecraft;

[TestClass]
public sealed class MicrosoftLoginRegressionTest
{
    private const string ClientId = "260c9f33-c462-4dcc-b49f-fc495adad16e";

    [TestMethod]
    public void LocalConfigurationRoundTripsAndRejectsInvalidReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BCCL-OAuth-" + Guid.NewGuid());
        var path = Path.Combine(directory, "MicrosoftOAuth.json");
        try
        {
            Assert.AreEqual("", MicrosoftOAuthConfiguration.Read(path));
            MicrosoftOAuthConfiguration.Save(path, "  " + ClientId.ToUpperInvariant() + "  ");
            Assert.AreEqual(ClientId, MicrosoftOAuthConfiguration.Read(path));
            Assert.ThrowsExactly<ArgumentException>(() => MicrosoftOAuthConfiguration.Save(path, Guid.Empty.ToString()));
            Assert.ThrowsExactly<ArgumentException>(() => MicrosoftOAuthConfiguration.Save(path, "not-an-app-id"));
            Assert.AreEqual(ClientId, MicrosoftOAuthConfiguration.Read(path));
            Assert.HasCount(1, Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task DevicePollingPreservesPendingErrorsAndRequiresRefreshTokenOnSuccess()
    {
        using var handler = new ScriptedHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            StringAssert.Contains(body, "client_id=" + ClientId);
            return Json(HttpStatusCode.BadRequest, "{\"error\":\"authorization_pending\"}");
        });
        using var client = new HttpClient(handler);
        var provider = new MicrosoftProvider(ClientId, client);
        var pending = await provider.PollDeviceCodeAsync(new DeviceCodeData { DeviceCode = "test-device" }, default);
        Assert.AreEqual("authorization_pending", pending!.Error);
        handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK,
            "{\"access_token\":\"oauth\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
        await Assert.ThrowsAsync<IdentityModelAuthenticationException>(() =>
            provider.PollDeviceCodeAsync(new DeviceCodeData { DeviceCode = "test-device" }, default));
    }

    [TestMethod]
    public async Task CompleteLoginUsesXboxXstsEntitlementsAndJavaProfile()
    {
        var requests = new List<string>();
        using var handler = new ScriptedHandler(async request =>
        {
            var host = request.RequestUri!.Host;
            var path = request.RequestUri.AbsolutePath;
            requests.Add(host + path);
            if (host == "user.auth.xboxlive.com")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.AreEqual("d=oauth", body["Properties"]!["RpsTicket"]!.ToString());
                return Xbox("xbl");
            }
            if (host == "xsts.auth.xboxlive.com")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.AreEqual("rp://api.minecraftservices.com/", body["RelyingParty"]!.ToString());
                Assert.AreEqual("xbl", body["Properties"]!["UserTokens"]![0]!.ToString());
                return Xbox("xsts");
            }
            if (path.EndsWith("login_with_xbox"))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.AreEqual("XBL3.0 x=hash;xsts", body["identityToken"]!.ToString());
                return Json(HttpStatusCode.OK, "{\"username\":\"account\",\"roles\":[],\"access_token\":\"minecraft\",\"token_type\":\"Bearer\",\"expires_in\":86400}");
            }
            Assert.AreEqual("Bearer minecraft", request.Headers.Authorization!.ToString());
            return path.EndsWith("mcstore")
                ? Json(HttpStatusCode.OK, "{\"items\":[{\"name\":\"game_minecraft\"}]}")
                : Json(HttpStatusCode.OK, "{\"id\":\"0123456789abcdef0123456789abcdef\",\"name\":\"TestPlayer\"}");
        });
        using var client = new HttpClient(handler);
        var provider = new MicrosoftProvider(ClientId, client);
        var result = await provider.CompleteAsync(new AuthorizeResult { AccessToken = "oauth", RefreshToken = "refresh" }, default);
        Assert.AreEqual("TestPlayer", result.UserName);
        Assert.AreEqual("refresh", result.RefreshToken);
        Assert.AreEqual("minecraft", result.AccessToken);
        Assert.IsTrue(result.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
        Assert.HasCount(5, requests);
    }

    [TestMethod]
    public async Task NotOwnedAndXstsFailuresNeverCreateSuccessfulLogin()
    {
        using var handler = new ScriptedHandler(request => Task.FromResult(
            request.RequestUri!.Host.Contains("xboxlive") ? Xbox("token") :
            request.RequestUri.AbsolutePath.EndsWith("login_with_xbox")
                ? Json(HttpStatusCode.OK, "{\"username\":\"account\",\"roles\":[],\"access_token\":\"minecraft\",\"token_type\":\"Bearer\",\"expires_in\":86400}")
                : Json(HttpStatusCode.OK, "{\"items\":[]}")));
        using var client = new HttpClient(handler);
        var provider = new MicrosoftProvider(ClientId, client);
        var error = await Assert.ThrowsAsync<IdentityModelAuthenticationException>(() =>
            provider.CompleteAsync(new AuthorizeResult { AccessToken = "oauth" }, default));
        Assert.AreEqual("minecraft_not_owned", error.Error);
        handler.Respond = request => Task.FromResult(request.RequestUri!.Host.StartsWith("xsts")
            ? Json(HttpStatusCode.Unauthorized, "{\"XErr\":2148916238}") : Xbox("token"));
        error = await Assert.ThrowsAsync<IdentityModelAuthenticationException>(() =>
            provider.CompleteAsync(new AuthorizeResult { AccessToken = "oauth" }, default));
        Assert.AreEqual("xsts_2148916238", error.Error);
    }

    [TestMethod]
    public async Task ReadingCancelledResponsePreservesCancellation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new CancelledContent() };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => response.AsStringAsync(cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => response.AsStreamAsync(cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => response.AsByteArrayAsync(cancelled.Token));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Xbox(string token) => Json(HttpStatusCode.OK,
        "{\"IssueInstant\":\"2026-11-08T00:00:00Z\",\"NotAfter\":\"2026-11-09T00:00:00Z\",\"Token\":\"" + token + "\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"hash\"}]}}");

    private sealed class ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Respond(request);
    }

    private sealed class CancelledContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromCanceled(new CancellationToken(true));
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromCanceled<Stream>(new CancellationToken(true));
    }
}
