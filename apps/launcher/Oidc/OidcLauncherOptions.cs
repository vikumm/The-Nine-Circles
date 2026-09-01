namespace Divinity.Launcher.Oidc;

public sealed class OidcLauncherOptions
{
    public Uri Authority { get; init; } = new("http://127.0.0.1:8080/realms/divinity-dev");
    public string ClientId { get; init; } = "divinity-launcher-dev";
    public string Scope { get; init; } = "openid profile";
    public string CallbackHost { get; init; } = "127.0.0.1";
    public string CallbackPath { get; init; } = "/callback";
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public static OidcLauncherOptions FromEnvironment() => new()
    {
        Authority = new Uri(GetEnvironment("DIVINITY_LAUNCHER_OIDC_AUTHORITY", "http://127.0.0.1:8080/realms/divinity-dev")),
        ClientId = GetEnvironment("DIVINITY_LAUNCHER_OIDC_CLIENT_ID", "divinity-launcher-dev"),
        Scope = GetEnvironment("DIVINITY_LAUNCHER_OIDC_SCOPE", "openid profile"),
        CallbackHost = GetEnvironment("DIVINITY_LAUNCHER_CALLBACK_HOST", "127.0.0.1"),
        CallbackPath = GetEnvironment("DIVINITY_LAUNCHER_CALLBACK_PATH", "/callback"),
        LoginTimeout = TimeSpan.FromSeconds(int.Parse(GetEnvironment("DIVINITY_LAUNCHER_LOGIN_TIMEOUT_SECONDS", "120")))
    };

    private static string GetEnvironment(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
