using Divinity.Launcher.Oidc;
using Divinity.Launcher.Session;

namespace Divinity.Launcher;

public sealed class LauncherAuthService
{
    private readonly OidcAuthFlow _authFlow;
    private readonly ISystemBrowserLauncher _browserLauncher;
    private readonly ILauncherStateStore _stateStore;
    private readonly ILauncherLog _log;
    private LauncherLoginSession? _activeSession;

    public LauncherAuthService(OidcAuthFlow authFlow, ISystemBrowserLauncher browserLauncher, ILauncherStateStore stateStore, ILauncherLog log)
    {
        _authFlow = authFlow;
        _browserLauncher = browserLauncher;
        _stateStore = stateStore;
        _log = log;
    }

    public async Task<LocalLauncherState> LoginAsync(OidcLauncherOptions options, CancellationToken cancellationToken)
    {
        _activeSession = await _authFlow.LoginAsync(options, cancellationToken);
        await _stateStore.SaveAsync(_activeSession.LocalState, cancellationToken);
        _log.Info("Local launcher login state saved without tokens.");
        return _activeSession.LocalState;
    }

    public async Task LogoutAsync(OidcLauncherOptions options, CancellationToken cancellationToken)
    {
        await _stateStore.ClearAsync(cancellationToken);
        _log.Info("Local launcher login state cleared.");

        try
        {
            await _authFlow.LogoutProviderAsync(options, _activeSession?.EndSessionEndpoint, _activeSession?.TokenSet.IdToken, _browserLauncher, cancellationToken);
        }
        finally
        {
            _activeSession = null;
        }
    }

    public Task<LocalLauncherState?> ReadLocalStateAsync(CancellationToken cancellationToken) =>
        _stateStore.ReadAsync(cancellationToken);
}
