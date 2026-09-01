using System.Security.Cryptography;
using Divinity.Launcher.Security;
using Divinity.Launcher.Session;

namespace Divinity.Launcher.Oidc;

public sealed class OidcAuthFlow
{
    private readonly OidcDiscoveryClient _discoveryClient;
    private readonly OidcTokenClient _tokenClient;
    private readonly ISystemBrowserLauncher _browserLauncher;
    private readonly ILauncherLog _log;

    public OidcAuthFlow(HttpClient httpClient, ISystemBrowserLauncher browserLauncher, ILauncherLog log)
    {
        _discoveryClient = new OidcDiscoveryClient(httpClient);
        _tokenClient = new OidcTokenClient(httpClient);
        _browserLauncher = browserLauncher;
        _log = log;
    }

    public async Task<LauncherLoginSession> LoginAsync(OidcLauncherOptions options, CancellationToken cancellationToken)
    {
        _log.Info("Starting OIDC PKCE login.");
        var discovery = await _discoveryClient.GetAsync(options.Authority, cancellationToken);
        var pkce = PkceGenerator.Create();
        var state = CreateOpaqueValue();
        var nonce = CreateOpaqueValue();

        await using var callbackListener = LoopbackCallbackListener.Start(options.CallbackHost, options.CallbackPath, state);
        var authorizationUri = BuildAuthorizationUri(discovery.AuthorizationEndpointUri, options, callbackListener.RedirectUri, pkce, state, nonce);

        _log.Info("Opening system browser for identity provider login.");
        var browserTask = _browserLauncher.OpenAsync(authorizationUri, cancellationToken);
        var callbackTask = callbackListener.WaitForCodeAsync(options.LoginTimeout, cancellationToken);

        var completedTask = await Task.WhenAny(browserTask, callbackTask);
        if (completedTask == browserTask && browserTask.IsFaulted)
        {
            await browserTask;
        }

        var authorizationCode = await callbackTask;
        await browserTask;
        _log.Info("Authorization callback accepted. Exchanging code with PKCE verifier.");

        var tokenSet = await _tokenClient.ExchangeAuthorizationCodeAsync(
            discovery.TokenEndpointUri,
            options.ClientId,
            authorizationCode,
            pkce.CodeVerifier,
            callbackListener.RedirectUri,
            cancellationToken);

        _log.Info("Launcher account login completed.");
        return new LauncherLoginSession(
            new LocalLauncherState(options.Authority.ToString(), options.ClientId, DateTimeOffset.UtcNow),
            tokenSet,
            discovery.EndSessionEndpointUri);
    }

    public async Task LogoutProviderAsync(OidcLauncherOptions options, Uri? endSessionEndpoint, string? idTokenHint, ISystemBrowserLauncher browserLauncher, CancellationToken cancellationToken)
    {
        if (endSessionEndpoint is null)
        {
            var discovery = await _discoveryClient.GetAsync(options.Authority, cancellationToken);
            endSessionEndpoint = discovery.EndSessionEndpointUri;
        }

        if (endSessionEndpoint is null)
        {
            _log.Warning("Identity provider does not advertise an end_session_endpoint; local state was cleared only.");
            return;
        }

        var logoutUri = BuildLogoutUri(endSessionEndpoint, options.ClientId, idTokenHint);
        _log.Info("Opening system browser for identity provider logout.");
        await browserLauncher.OpenAsync(logoutUri, cancellationToken);
    }

    private static Uri BuildAuthorizationUri(Uri authorizationEndpoint, OidcLauncherOptions options, Uri redirectUri, PkcePair pkce, string state, string nonce)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["scope"] = options.Scope,
            ["code_challenge"] = pkce.CodeChallenge,
            ["code_challenge_method"] = pkce.Method,
            ["state"] = state,
            ["nonce"] = nonce
        };

        return AppendQuery(authorizationEndpoint, query);
    }

    private static Uri BuildLogoutUri(Uri endSessionEndpoint, string clientId, string? idTokenHint)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId
        };

        if (!string.IsNullOrWhiteSpace(idTokenHint))
        {
            query["id_token_hint"] = idTokenHint;
        }

        return AppendQuery(endSessionEndpoint, query);
    }

    private static Uri AppendQuery(Uri baseUri, IReadOnlyDictionary<string, string> values)
    {
        var builder = new UriBuilder(baseUri);
        var prefix = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = prefix + string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri;
    }

    private static string CreateOpaqueValue() => Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
}

public sealed record LauncherLoginSession(LocalLauncherState LocalState, OidcTokenSet TokenSet, Uri? EndSessionEndpoint);

public sealed class LauncherAuthException : Exception
{
    public LauncherAuthException(string message)
        : base(message)
    {
    }

    public LauncherAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
