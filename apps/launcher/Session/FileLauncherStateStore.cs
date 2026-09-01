using System.Text.Json;

namespace Divinity.Launcher.Session;

public sealed class FileLauncherStateStore : ILauncherStateStore
{
    private readonly string _path;

    public FileLauncherStateStore(string path)
    {
        _path = path;
    }

    public static FileLauncherStateStore FromEnvironment()
    {
        var configuredPath = Environment.GetEnvironmentVariable("DIVINITY_LAUNCHER_STATE_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return new FileLauncherStateStore(configuredPath);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new FileLauncherStateStore(Path.Combine(home, ".divinity", "launcher", "session-state.json"));
    }

    public async Task<LocalLauncherState?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        return JsonSerializer.Deserialize<LocalLauncherState>(json);
    }

    public async Task SaveAsync(LocalLauncherState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json + Environment.NewLine, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return Task.CompletedTask;
    }
}
