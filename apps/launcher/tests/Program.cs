using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Divinity.Launcher;
using Divinity.Launcher.Oidc;
using Divinity.Launcher.Security;
using Divinity.Launcher.Session;

var checks = new List<LauncherCheck>();

checks.Add(Check("PKCE generation and validation", PkceGenerationIsValid()));
checks.Add(Check("invalid state is rejected", InvalidStateIsRejected()));
checks.Add(Check("duplicate callback is rejected", DuplicateCallbackIsRejected()));
checks.Add(await CheckAsync("local OIDC login completes without password capture", LocalOidcLoginCompletesAsync));
checks.Add(await CheckAsync("logout clears local state", LogoutClearsLocalStateAsync));
checks.Add(await CheckAsync("logs do not contain tokens or authorization code", LogsDoNotContainSecretsAsync));
checks.Add(await CheckAsync("Keycloak dev login", KeycloakDevLoginAsync));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-005 launcher tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-005 launcher tests passed.");
return 0;

static bool PkceGenerationIsValid()
{
    var pair = PkceGenerator.Create();
    return PkceGenerator.Validate(pair)
        && pair.CodeVerifier.Length is >= 43 and <= 128
        && pair.CodeChallenge.Length > 0
        && pair.Method == PkceGenerator.ChallengeMethod;
}

static bool InvalidStateIsRejected()
{
    var state = new LoopbackCallbackState("expected-state", "/callback");
    var result = state.Accept(new Uri("http://127.0.0.1:45123/callback?code=code-1&state=wrong-state"));

    return !result.Success && result.Status == CallbackValidationStatus.InvalidState;
}

static bool DuplicateCallbackIsRejected()
{
    var state = new LoopbackCallbackState("expected-state", "/callback");
    var accepted = state.Accept(new Uri("http://127.0.0.1:45123/callback?code=code-1&state=expected-state"));
    var duplicated = state.Accept(new Uri("http://127.0.0.1:45123/callback?code=code-2&state=expected-state"));

    return accepted.Success && !duplicated.Success && duplicated.Status == CallbackValidationStatus.DuplicateCallback;
}

static async Task<bool> LocalOidcLoginCompletesAsync()
{
    await using var provider = await FakeOidcProvider.StartAsync();
    using var httpClient = new HttpClient();
    var browser = new HttpClientBrowser();
    var log = new InMemoryLauncherLog();
    var flow = new OidcAuthFlow(httpClient, browser, log);
    var store = new InMemoryLauncherStateStore();
    var service = new LauncherAuthService(flow, browser, store, log);

    var state = await service.LoginAsync(provider.CreateOptions(), CancellationToken.None);
    var storedState = await store.ReadAsync(CancellationToken.None);

    return storedState is not null
        && string.Equals(state.Authority, storedState.Authority, StringComparison.Ordinal)
        && provider.ReceivedPkceS256
        && provider.ReceivedNonce
        && provider.TokenExchangeSucceeded;
}

static async Task<bool> LogoutClearsLocalStateAsync()
{
    await using var provider = await FakeOidcProvider.StartAsync();
    using var httpClient = new HttpClient();
    var browser = new HttpClientBrowser();
    var log = new InMemoryLauncherLog();
    var flow = new OidcAuthFlow(httpClient, browser, log);
    var store = new InMemoryLauncherStateStore();
    var service = new LauncherAuthService(flow, browser, store, log);
    var options = provider.CreateOptions();

    await service.LoginAsync(options, CancellationToken.None);
    await service.LogoutAsync(options, CancellationToken.None);

    return await store.ReadAsync(CancellationToken.None) is null && provider.LogoutCalled;
}

static async Task<bool> LogsDoNotContainSecretsAsync()
{
    await using var provider = await FakeOidcProvider.StartAsync();
    using var httpClient = new HttpClient();
    var browser = new HttpClientBrowser();
    var log = new InMemoryLauncherLog();
    var flow = new OidcAuthFlow(httpClient, browser, log);
    var store = new InMemoryLauncherStateStore();
    var service = new LauncherAuthService(flow, browser, store, log);

    await service.LoginAsync(provider.CreateOptions(), CancellationToken.None);

    return !SensitiveLogSanitizer.ContainsSensitiveValue(
        log.Entries,
        FakeOidcProvider.AuthorizationCode,
        FakeOidcProvider.AccessToken,
        FakeOidcProvider.RefreshToken,
        FakeOidcProvider.IdToken);
}

static async Task<bool> KeycloakDevLoginAsync()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("DIVINITY_RUN_KEYCLOAK_LOGIN_TEST"), "true", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("SKIP Keycloak dev login: set DIVINITY_RUN_KEYCLOAK_LOGIN_TEST=true to run against local Keycloak.");
        return true;
    }

    var authority = Environment.GetEnvironmentVariable("DIVINITY_LAUNCHER_OIDC_AUTHORITY") ?? "http://127.0.0.1:8080/realms/divinity-dev";
    var username = Environment.GetEnvironmentVariable("DIVINITY_KEYCLOAK_DEV_USERNAME") ?? "divinity.dev";
    var password = Environment.GetEnvironmentVariable("DIVINITY_KEYCLOAK_DEV_PASSWORD") ?? "divinity_dev_password";

    using var httpClient = new HttpClient();
    var browser = new KeycloakFormBrowser(username, password);
    var log = new InMemoryLauncherLog();
    var options = new OidcLauncherOptions
    {
        Authority = new Uri(authority),
        ClientId = Environment.GetEnvironmentVariable("DIVINITY_LAUNCHER_OIDC_CLIENT_ID") ?? "divinity-launcher-dev",
        LoginTimeout = TimeSpan.FromSeconds(90)
    };
    var flow = new OidcAuthFlow(httpClient, browser, log);
    var store = new InMemoryLauncherStateStore();
    var service = new LauncherAuthService(flow, browser, store, log);

    try
    {
        await service.LoginAsync(options, CancellationToken.None);
        await service.LogoutAsync(options, CancellationToken.None);

        return await store.ReadAsync(CancellationToken.None) is null
            && !SensitiveLogSanitizer.ContainsSensitiveValue(log.Entries, "access_token", "refresh_token", "authorization_code");
    }
    catch (Exception ex) when (ex is HttpRequestException or LauncherAuthException or TimeoutException)
    {
        Console.Error.WriteLine($"Keycloak dev login failed: {ex.Message}");
        return false;
    }
}

static LauncherCheck Check(string name, bool passed) => new(name, passed);

static async Task<LauncherCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new LauncherCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new LauncherCheck(name, false);
    }
}

internal readonly record struct LauncherCheck(string Name, bool Passed);

internal sealed class HttpClientBrowser : ISystemBrowserLauncher
{
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        CookieContainer = new CookieContainer()
    });

    public async Task OpenAsync(Uri authorizationUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(authorizationUri, cancellationToken);
        _ = response.EnsureSuccessStatusCode();
    }
}

internal sealed class KeycloakFormBrowser : ISystemBrowserLauncher
{
    private static readonly Regex FormActionPattern = new("<form[^>]*action=\"(?<action>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly string _username;
    private readonly string _password;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public KeycloakFormBrowser(string username, string password)
    {
        _username = username;
        _password = password;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
    }

    public async Task OpenAsync(Uri authorizationUri, CancellationToken cancellationToken)
    {
        if (authorizationUri.AbsolutePath.EndsWith("/protocol/openid-connect/auth", StringComparison.Ordinal))
        {
            await CompleteLoginAsync(authorizationUri, cancellationToken);
            return;
        }

        await FollowRedirectsAsync(authorizationUri, cancellationToken);
    }

    private async Task CompleteLoginAsync(Uri authorizationUri, CancellationToken cancellationToken)
    {
        using var loginPageResponse = await FollowRedirectsAsync(authorizationUri, cancellationToken, stopAtSuccess: true);
        var loginHtml = await loginPageResponse.Content.ReadAsStringAsync(cancellationToken);
        var formAction = ExtractLoginFormAction(loginHtml, authorizationUri);

        using var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = _username,
            ["password"] = _password,
            ["credentialId"] = string.Empty
        });

        using var response = await SendAsync(HttpMethod.Post, formAction, formContent, cancellationToken);
        await FollowRedirectsAsync(response, cancellationToken);
    }

    private static Uri ExtractLoginFormAction(string html, Uri authorizationUri)
    {
        var match = FormActionPattern.Match(html);
        if (!match.Success)
        {
            throw new LauncherAuthException("Keycloak login page did not contain an expected login form.");
        }

        var decodedAction = WebUtility.HtmlDecode(match.Groups["action"].Value);
        return new Uri(authorizationUri, decodedAction);
    }

    private async Task<HttpResponseMessage> FollowRedirectsAsync(Uri uri, CancellationToken cancellationToken, bool stopAtSuccess = false)
    {
        var response = await SendAsync(HttpMethod.Get, uri, content: null, cancellationToken);
        return await FollowRedirectsAsync(response, cancellationToken, stopAtSuccess);
    }

    private async Task<HttpResponseMessage> FollowRedirectsAsync(HttpResponseMessage response, CancellationToken cancellationToken, bool stopAtSuccess = false)
    {
        var current = response;
        for (var redirectCount = 0; redirectCount < 12; redirectCount++)
        {
            if (!IsRedirect(current.StatusCode))
            {
                if (stopAtSuccess && current.IsSuccessStatusCode)
                {
                    return current;
                }

                if (!current.IsSuccessStatusCode)
                {
                    throw new LauncherAuthException($"Keycloak browser step failed with HTTP {(int)current.StatusCode}. Check the dev realm, redirect URI and test user.");
                }

                return current;
            }

            var next = current.Headers.Location ?? throw new LauncherAuthException("Keycloak redirect was missing Location header.");
            var nextUri = next.IsAbsoluteUri ? next : new Uri(current.RequestMessage!.RequestUri!, next);
            current.Dispose();
            current = await SendAsync(HttpMethod.Get, nextUri, content: null, cancellationToken);
        }

        throw new LauncherAuthException("Keycloak login exceeded the redirect limit.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };

        if (_cookies.Count > 0 && IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address))
        {
            request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        CaptureCookies(response);
        return response;
    }

    private void CaptureCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        foreach (var setCookie in setCookies)
        {
            var cookiePair = setCookie.Split(';', 2)[0];
            var parts = cookiePair.Split('=', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                _cookies[parts[0]] = parts[1];
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}

internal sealed class FakeOidcProvider : IAsyncDisposable
{
    public const string AuthorizationCode = "fake-authorization-code-secret";
    public const string AccessToken = "fake-access-token-secret";
    public const string RefreshToken = "fake-refresh-token-secret";
    public const string IdToken = "fake-id-token-secret";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;
    private string? _codeChallenge;

    private FakeOidcProvider(int port)
    {
        Authority = new Uri($"http://127.0.0.1:{port}/realms/divinity-dev");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serverTask = Task.Run(ServeAsync);
    }

    public Uri Authority { get; }
    public bool ReceivedPkceS256 { get; private set; }
    public bool ReceivedNonce { get; private set; }
    public bool TokenExchangeSucceeded { get; private set; }
    public bool LogoutCalled { get; private set; }

    public static Task<FakeOidcProvider> StartAsync() =>
        Task.FromResult(new FakeOidcProvider(GetFreeLoopbackPort()));

    public OidcLauncherOptions CreateOptions() => new()
    {
        Authority = Authority,
        ClientId = "divinity-launcher-dev",
        LoginTimeout = TimeSpan.FromSeconds(15)
    };

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Close();

        try
        {
            await _serverTask;
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _cts.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var context = await _listener.GetContextAsync();
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;
        if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context.Response, new
            {
                issuer = Authority.ToString().TrimEnd('/'),
                authorization_endpoint = $"{Authority.ToString().TrimEnd('/')}/protocol/openid-connect/auth",
                token_endpoint = $"{Authority.ToString().TrimEnd('/')}/protocol/openid-connect/token",
                end_session_endpoint = $"{Authority.ToString().TrimEnd('/')}/protocol/openid-connect/logout"
            });
            return;
        }

        if (path.EndsWith("/protocol/openid-connect/auth", StringComparison.Ordinal))
        {
            var query = QueryString.Parse(context.Request.Url.Query);
            _codeChallenge = query["code_challenge"];
            ReceivedPkceS256 = string.Equals(query["code_challenge_method"], "S256", StringComparison.Ordinal);
            ReceivedNonce = query.TryGetValue("nonce", out var nonce) && !string.IsNullOrWhiteSpace(nonce);
            var redirectUri = new Uri(query["redirect_uri"]);
            var callback = new UriBuilder(redirectUri)
            {
                Query = $"code={Uri.EscapeDataString(AuthorizationCode)}&state={Uri.EscapeDataString(query["state"])}"
            };
            context.Response.Redirect(callback.Uri.ToString());
            context.Response.Close();
            return;
        }

        if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
        {
            var form = QueryString.Parse(await ReadBodyAsync(context.Request));
            var validVerifier = _codeChallenge is not null
                && form.TryGetValue("code_verifier", out var verifier)
                && string.Equals(PkceGenerator.CreateChallenge(verifier), _codeChallenge, StringComparison.Ordinal);
            var validCode = form.TryGetValue("code", out var code)
                && string.Equals(code, AuthorizationCode, StringComparison.Ordinal);

            if (!validVerifier || !validCode)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            TokenExchangeSucceeded = true;
            await WriteJsonAsync(context.Response, new
            {
                access_token = AccessToken,
                refresh_token = RefreshToken,
                id_token = IdToken,
                expires_in = 300,
                token_type = "Bearer"
            });
            return;
        }

        if (path.EndsWith("/protocol/openid-connect/logout", StringComparison.Ordinal))
        {
            LogoutCalled = true;
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
    {
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return await reader.ReadToEndAsync();
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
