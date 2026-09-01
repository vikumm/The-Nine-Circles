using Divinity.Launcher;
using Divinity.Launcher.Oidc;
using Divinity.Launcher.Session;

var command = args.FirstOrDefault() ?? "status";
var log = new ConsoleLauncherLog();
using var httpClient = new HttpClient();
var browser = new SystemBrowserLauncher();
var options = OidcLauncherOptions.FromEnvironment();
var stateStore = FileLauncherStateStore.FromEnvironment();
var authFlow = new OidcAuthFlow(httpClient, browser, log);
var authService = new LauncherAuthService(authFlow, browser, stateStore, log);

try
{
    switch (command)
    {
        case "status":
            var state = await authService.ReadLocalStateAsync(CancellationToken.None);
            Console.WriteLine($"{LauncherInfo.ComponentName}: {LauncherInfo.Status}");
            Console.WriteLine(state is null
                ? "login: not authenticated"
                : $"login: authenticated with {state.Authority} at {state.AuthenticatedAtUtc:O}");
            return 0;

        case "login":
            var loginState = await authService.LoginAsync(options, CancellationToken.None);
            Console.WriteLine($"login: authenticated with {loginState.Authority}");
            return 0;

        case "logout":
            await authService.LogoutAsync(options, CancellationToken.None);
            Console.WriteLine("logout: local state cleared");
            return 0;

        default:
            PrintUsage();
            return 2;
    }
}
catch (TimeoutException ex)
{
    Console.Error.WriteLine($"Login failed: {ex.Message}");
    return 1;
}
catch (LauncherAuthException ex)
{
    Console.Error.WriteLine($"Login failed: {ex.Message}");
    return 1;
}
catch (UriFormatException ex)
{
    Console.Error.WriteLine($"Login failed: invalid OIDC configuration. {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  launcher status");
    Console.Error.WriteLine("  launcher login");
    Console.Error.WriteLine("  launcher logout");
}
