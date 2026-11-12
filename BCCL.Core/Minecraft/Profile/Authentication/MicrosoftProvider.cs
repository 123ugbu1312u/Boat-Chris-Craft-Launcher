using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BCCL.Core.App;
using BCCL.Core.IO.Net;
using BCCL.Core.IO.Net.Http;
using BCCL.Core.Minecraft.IdentityModel.OAuth;
using BCCL.Core.Minecraft.IdentityModel;
using BCCL.Core.Minecraft.Profile.Authentication.Utils;
using BCCL.Core.Minecraft.Profile.Authentication.Utils.Models;
using BCCL.Core.Minecraft.Profile.Models;

namespace BCCL.Core.Minecraft.Profile.Authentication;

/// <summary>
/// Microsoft consumer account provider. OAuth, Xbox Live, XSTS and Minecraft service calls
/// are kept here so launch code never needs to understand the six-step protocol.
/// </summary>
public sealed class MicrosoftProvider : IAuthenticateProvider
{
    private const string DeviceEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    private readonly string _clientId;
    private readonly HttpClient? _httpClient;

    public MicrosoftProvider(string? clientId = null, HttpClient? httpClient = null)
    {
        _httpClient = httpClient;
        _clientId = string.IsNullOrWhiteSpace(clientId) ? Secrets.MSOAuthClientId : clientId.Trim();
        if (string.IsNullOrWhiteSpace(_clientId))
            throw new IdentityModelConfigurationException("Microsoft OAuth client id is not configured.");
        _clientId = MicrosoftOAuthConfiguration.Normalize(_clientId);
    }

    public async Task<DeviceCodeData> GetDeviceCodeAsync(CancellationToken token)
    {
        using var response = await HttpRequest.CreatePost(DeviceEndpoint)
            .WithContent(new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["scope"] = "XboxLive.signin offline_access"
            }))
            .SendAsync(_httpClient ?? NetworkService.GetClient(NetworkService.MicrosoftEntraId),
                addMetedata: _httpClient is null, cancellationToken: token)
            .ConfigureAwait(false);
        var result = await response.AsJsonAsync<DeviceCodeData>(cancellationToken: token).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Microsoft device authorization response was empty.");
        if (result.IsError) throw new IdentityModelAuthenticationException(result.Error, result.ErrorDescription);
        result.Validate();
        return result;
    }

    public async Task<AuthorizeResult?> PollDeviceCodeAsync(DeviceCodeData data, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var response = await HttpRequest.CreatePost(TokenEndpoint)
            .WithContent(new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = data.DeviceCode ?? string.Empty
            }))
            .SendAsync(_httpClient ?? NetworkService.GetClient(NetworkService.MicrosoftEntraId),
                addMetedata: _httpClient is null, cancellationToken: token)
            .ConfigureAwait(false);
        var result = await response.AsJsonAsync<AuthorizeResult>(cancellationToken: token).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Microsoft token response was empty.");
        result.Validate(requireRefreshToken: true);
        return result;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var oauth = request.OAuthResult;
        if (oauth is null && !string.IsNullOrWhiteSpace(request.RefreshToken))
            oauth = await RefreshOAuthAsync(request.RefreshToken, token).ConfigureAwait(false);
        if (oauth is null && request.DeviceCodeHandler is not null)
        {
            var device = await GetDeviceCodeAsync(token).ConfigureAwait(false);
            oauth = await request.DeviceCodeHandler(
                new DeviceCodeAuthenticationContext(device, pollToken => PollDeviceCodeAsync(device, pollToken)), token)
                .ConfigureAwait(false);
        }
        if (oauth is null)
            throw new IdentityModelConfigurationException("Microsoft login requires an OAuth result or device-code handler.");
        return await CompleteAsync(oauth, token).ConfigureAwait(false);
    }

    public async Task<AuthenticationResult> RefreshAsync(McProfile profile, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profile.RefreshToken))
            throw new IdentityModelAuthenticationException("invalid_grant", "Microsoft refresh token is empty.");
        return await AuthenticateAsync(new AuthenticationRequest { RefreshToken = profile.RefreshToken }, token)
            .ConfigureAwait(false);
    }

    public async Task<bool> ValidateAsync(McProfile profile, CancellationToken token)
    {
        if (profile.IsExpired || string.IsNullOrWhiteSpace(profile.AccessToken)) return false;
        return await MojangUtils.CheckLicenseAsync(profile, token, _httpClient).ConfigureAwait(false);
    }

    public async Task<AuthorizeResult> RefreshOAuthAsync(string refreshToken, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        using var response = await HttpRequest.CreatePost(TokenEndpoint)
            .WithContent(new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = "XboxLive.signin offline_access"
            }))
            .SendAsync(_httpClient ?? NetworkService.GetClient(NetworkService.MicrosoftEntraId),
                addMetedata: _httpClient is null, cancellationToken: token)
            .ConfigureAwait(false);
        var result = await response.AsJsonAsync<AuthorizeResult>(cancellationToken: token).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Microsoft token response was empty.");
        if (result.IsError) throw new IdentityModelAuthenticationException(result.Error, result.ErrorDescription);
        result.Validate();
        return result with { RefreshToken = result.RefreshToken ?? refreshToken };
    }

    public async Task<AuthenticationResult> CompleteAsync(AuthorizeResult oauth, CancellationToken token)
    {
        if (oauth.IsError) throw new IdentityModelAuthenticationException(oauth.Error, oauth.ErrorDescription);
        if (string.IsNullOrWhiteSpace(oauth.AccessToken))
            throw new IdentityModelAuthenticationException("invalid_token", "Microsoft OAuth did not return an access token.");

        var xbl = await XboxUtils.AuthenticateAsync(new XboxAuthenticate<XboxProperty>
        {
            Properties = new XboxProperty { RpsTicket = $"d={oauth.AccessToken}" },
            TokenType = "JWT"
        }, isXsts: false, token, _httpClient).ConfigureAwait(false)
                  ?? throw new IdentityModelAuthenticationException("xbox_authentication_failed", "Xbox Live returned no token.");

        var xsts = await XboxUtils.AuthenticateAsync(new XboxAuthenticate<XSTSProperty>
        {
            Properties = new XSTSProperty { UserTokens = [xbl.Token] },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, isXsts: true, token, _httpClient).ConfigureAwait(false)
                   ?? throw new IdentityModelAuthenticationException("xsts_authentication_failed", "XSTS returned no token.");

        var userHash = xsts.DisplayClaims?.Xui is { Count: > 0 } ? xsts.DisplayClaims.Xui[0].UserHash : null;
        if (string.IsNullOrWhiteSpace(userHash))
            throw new IdentityModelAuthenticationException("xsts_missing_user_hash", "XSTS response did not contain a user hash.");

        var minecraft = await MojangUtils.AuthenticateAsync(xsts.Token, userHash, token, _httpClient).ConfigureAwait(false)
                        ?? throw new IdentityModelAuthenticationException("minecraft_authentication_failed", "Minecraft authentication returned no token.");
        if (string.IsNullOrWhiteSpace(minecraft.AccessToken))
            throw new IdentityModelAuthenticationException("minecraft_authentication_failed", "Minecraft access token is empty.");

        var safe = new SafeProfile
        {
            UserName = minecraft.UserName,
            AccessToken = minecraft.AccessToken,
            Uuid = string.Empty,
            TokenType = minecraft.TokenType
        };
        if (!await MojangUtils.CheckLicenseAsync(safe, token, _httpClient).ConfigureAwait(false))
            throw new IdentityModelAuthenticationException("minecraft_not_owned", "The Microsoft account does not own Minecraft.");

        var profile = await MojangUtils.GetProfileAsync(safe, token, _httpClient).ConfigureAwait(false);
        return new AuthenticationResult
        {
            ProfileType = ProfileType.Microsoft,
            UserName = profile.Name,
            Uuid = profile.Uuid,
            AccessToken = minecraft.AccessToken,
            RefreshToken = oauth.RefreshToken ?? string.Empty,
            ClientToken = profile.Uuid,
            TokenType = minecraft.TokenType,
            ExpiresAt = minecraft.ExpiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(minecraft.ExpiresIn) : null,
            RawJson = System.Text.Json.JsonSerializer.Serialize(profile),
            Provider = "microsoft"
        };
    }
}
