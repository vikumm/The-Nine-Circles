using System.Diagnostics;

namespace Divinity.Launcher.Oidc;

public interface ISystemBrowserLauncher
{
    Task OpenAsync(Uri authorizationUri, CancellationToken cancellationToken);
}

public sealed class SystemBrowserLauncher : ISystemBrowserLauncher
{
    public Task OpenAsync(Uri authorizationUri, CancellationToken cancellationToken)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authorizationUri.ToString(),
                UseShellExecute = true
            });

            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new LauncherAuthException("Unable to open the system browser. Open the launcher on a desktop session and retry.", ex);
        }
    }
}
