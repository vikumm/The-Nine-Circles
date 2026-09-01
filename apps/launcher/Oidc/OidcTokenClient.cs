using System.Text.Json;
using System.Text.Json.Serialization;

namespace Divinity.Launcher.Oidc;

public sealed class OidcTokenClient
{
    private readonly HttpClient _httpClient;

    public OidcTokenClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OidcTokenSet> ExchangeAuthorizationCodeAsync(
        Uri tokenEndpoint,
        string clientId,
        string authorizationCode,
        string codeVerifier,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = authorizationCode,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri.ToString()
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LauncherAuthException("Token exchange failed because the identity provider is unavailable.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new LauncherAuthException($"Token exchange failed with HTTP {(int)response.StatusCode}. Retry login and check Keycloak client redirect URIs.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenSet = JsonSerializer.Deserialize<OidcTokenSet>(json)
            ?? throw new LauncherAuthException("Token exchange returned an empty response.");

        if (string.IsNullOrWhiteSpace(tokenSet.AccessToken))
        {
            throw new LauncherAuthException("Token exchange did not return an access token.");
        }

        return tokenSet;
    }
}

public sealed class OidcTokenSet
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = string.Empty;
}
