using System.Text.Json;

namespace Divinity.Launcher.Oidc;

public sealed class OidcDiscoveryClient
{
    private readonly HttpClient _httpClient;

    public OidcDiscoveryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OidcDiscoveryDocument> GetAsync(Uri authority, CancellationToken cancellationToken)
    {
        var discoveryUri = new Uri($"{authority.AbsoluteUri.TrimEnd('/')}/.well-known/openid-configuration");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(discoveryUri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LauncherAuthException($"Identity provider unavailable at {authority}. Check Keycloak or DIVINITY_LAUNCHER_OIDC_AUTHORITY.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new LauncherAuthException($"Identity provider discovery failed with HTTP {(int)response.StatusCode}. Check Keycloak realm configuration.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = JsonSerializer.Deserialize<OidcDiscoveryDocument>(json)
            ?? throw new LauncherAuthException("Identity provider discovery returned an empty document.");

        if (string.IsNullOrWhiteSpace(document.AuthorizationEndpoint) || string.IsNullOrWhiteSpace(document.TokenEndpoint))
        {
            throw new LauncherAuthException("Identity provider discovery is missing authorization_endpoint or token_endpoint.");
        }

        return document;
    }
}
