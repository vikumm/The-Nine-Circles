namespace Divinity.Launcher.Session;

public interface ILauncherStateStore
{
    Task<LocalLauncherState?> ReadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LocalLauncherState state, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
