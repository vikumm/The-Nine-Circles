namespace Divinity.Launcher.Session;

public sealed class InMemoryLauncherStateStore : ILauncherStateStore
{
    private LocalLauncherState? _state;

    public Task<LocalLauncherState?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_state);

    public Task SaveAsync(LocalLauncherState state, CancellationToken cancellationToken)
    {
        _state = state;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _state = null;
        return Task.CompletedTask;
    }
}
