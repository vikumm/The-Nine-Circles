using System.Text.Json.Serialization;

namespace Divinity.Launcher.Oidc;

public sealed class OidcDiscoveryDocument
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; init; } = string.Empty;

    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; init; } = string.Empty;

    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; init; } = string.Empty;

    [JsonPropertyName("end_session_endpoint")]
    public string? EndSessionEndpoint { get; init; }

    public Uri AuthorizationEndpointUri => new(AuthorizationEndpoint);
    public Uri TokenEndpointUri => new(TokenEndpoint);
    public Uri? EndSessionEndpointUri => string.IsNullOrWhiteSpace(EndSessionEndpoint) ? null : new Uri(EndSessionEndpoint);
}
