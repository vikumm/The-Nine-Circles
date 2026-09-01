using Divinity.Launcher.Security;

namespace Divinity.Launcher;

public interface ILauncherLog
{
    void Info(string message);
    void Warning(string message);
}

public sealed class ConsoleLauncherLog : ILauncherLog
{
    public void Info(string message) => Console.WriteLine(message);

    public void Warning(string message) => Console.Error.WriteLine(message);
}

public sealed class InMemoryLauncherLog : ILauncherLog
{
    private readonly List<string> _entries = [];
    private readonly List<string> _sensitiveValues = [];

    public IReadOnlyList<string> Entries => _entries;

    public void AddSensitiveValue(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _sensitiveValues.Add(value);
        }
    }

    public void Info(string message) => _entries.Add(SensitiveLogSanitizer.RedactKnownValues(message, _sensitiveValues));

    public void Warning(string message) => _entries.Add(SensitiveLogSanitizer.RedactKnownValues(message, _sensitiveValues));
}
