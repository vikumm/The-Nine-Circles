using System.Text.Json;

namespace Divinity.LoadBots;

public sealed record LoadBotReport(
    string TaskId,
    string Profile,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<int> ConfiguredRampSteps,
    int ConcurrentCombatBots,
    int ResilienceBots,
    int DisconnectPercent,
    IReadOnlyList<LoadBotCheck> Checks,
    IReadOnlyList<LoadBotResult> BotResults,
    IReadOnlyDictionary<string, int> MessagesByType,
    IReadOnlyList<LoadBotLatencySummary> Latency,
    IReadOnlyList<string> Inconsistencies,
    IReadOnlyList<string> Limitations)
{
    public bool Success =>
        Checks.All(check => check.Passed)
        && BotResults.All(result => result.Success)
        && Inconsistencies.Count == 0;

    public int SuccessfulBots => BotResults.Count(result => result.Success);

    public int FailedBots => BotResults.Count(result => !result.Success);
}

public sealed record LoadBotCheck(string Name, bool Passed, string Detail);

public sealed record LoadBotResult(
    string BotId,
    bool Success,
    IReadOnlyList<string> Failures,
    int DuplicateRewardRecords,
    bool ReplayRejected,
    bool Reconnected,
    bool EquippedItem,
    int RewardItemCount,
    int CurrencyBalance);

public sealed record LoadBotLatencySample(string Operation, long ElapsedMilliseconds);

public sealed record LoadBotLatencySummary(
    string Operation,
    int Count,
    double AverageMs,
    long P95Ms,
    long MaxMs);

public sealed class LoadBotReportBuilder
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _messagesByType = new(StringComparer.Ordinal);
    private readonly List<LoadBotLatencySample> _latency = [];
    private readonly List<LoadBotResult> _botResults = [];
    private readonly List<LoadBotCheck> _checks = [];
    private readonly List<string> _inconsistencies = [];
    private readonly DateTimeOffset _startedAtUtc;

    public LoadBotReportBuilder(DateTimeOffset startedAtUtc)
    {
        _startedAtUtc = startedAtUtc;
    }

    public void CountMessage(string messageType, int count = 1)
    {
        lock (_gate)
        {
            _messagesByType.TryGetValue(messageType, out var current);
            _messagesByType[messageType] = current + count;
        }
    }

    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var started = TimeProvider.System.GetTimestamp();
        try
        {
            return await action();
        }
        finally
        {
            AddLatency(operation, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public T Measure<T>(string operation, Func<T> action)
    {
        var started = TimeProvider.System.GetTimestamp();
        try
        {
            return action();
        }
        finally
        {
            AddLatency(operation, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public void AddBotResult(LoadBotResult result)
    {
        lock (_gate)
        {
            _botResults.Add(result);
            foreach (var failure in result.Failures)
            {
                _inconsistencies.Add($"{result.BotId}: {failure}");
            }
        }
    }

    public void AddCheck(string name, bool passed, string detail)
    {
        lock (_gate)
        {
            _checks.Add(new LoadBotCheck(name, passed, detail));
            if (!passed)
            {
                _inconsistencies.Add($"{name}: {detail}");
            }
        }
    }

    public LoadBotReport Build(LoadBotOptions options, DateTimeOffset completedAtUtc)
    {
        lock (_gate)
        {
            return new LoadBotReport(
                options.RunHardeningScenario || options.RunProfilingScenario || options.SoakDurationSeconds > 0 ? "VS-020" : "VS-019",
                options.Profile,
                _startedAtUtc,
                completedAtUtc,
                options.RampSteps.ToArray(),
                options.ConcurrentCombatBots,
                options.ResilienceBots,
                options.DisconnectPercent,
                _checks.ToArray(),
                _botResults.ToArray(),
                _messagesByType.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                SummarizeLatency(),
                _inconsistencies.ToArray(),
                [
                    "The bot runs without Unity rendering unless the Unity QA profiling command is run separately.",
                    "The in-process harness uses deterministic test stores under the OS temp directory.",
                    "Reward and equipment stores are still separate in the current vertical slice; the bot validates both paths without writing client-authoritative state.",
                    "The full soak profile defaults to 7200 seconds; CI should use a short smoke duration and the release gate should run the full profile."
                ]);
        }
    }

    private void AddLatency(string operation, double elapsedMilliseconds)
    {
        lock (_gate)
        {
            _latency.Add(new LoadBotLatencySample(operation, (long)Math.Round(elapsedMilliseconds)));
        }
    }

    private IReadOnlyList<LoadBotLatencySummary> SummarizeLatency() =>
        _latency
            .GroupBy(sample => sample.Operation)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var values = group.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
                var p95Index = Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * 0.95d) - 1);
                return new LoadBotLatencySummary(
                    group.Key,
                    values.Length,
                    values.Average(),
                    values[p95Index],
                    values[^1]);
            })
            .ToArray();
}

public static class LoadBotConsole
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(LoadBotReport report)
    {
        Console.WriteLine($"{report.TaskId} load bot report: {(report.Success ? "PASS" : "FAIL")}");
        Console.WriteLine($"Profile: {report.Profile}");
        Console.WriteLine($"Ramp: {string.Join(" -> ", report.ConfiguredRampSteps)}");
        Console.WriteLine($"Bots: {report.SuccessfulBots} succeeded, {report.FailedBots} failed");
        Console.WriteLine("Checks:");
        foreach (var check in report.Checks)
        {
            Console.WriteLine($"  {(check.Passed ? "PASS" : "FAIL")} {check.Name} - {check.Detail}");
        }

        Console.WriteLine("Messages:");
        foreach (var pair in report.MessagesByType)
        {
            Console.WriteLine($"  {pair.Key}: {pair.Value}");
        }

        Console.WriteLine("Latency:");
        foreach (var latency in report.Latency)
        {
            Console.WriteLine($"  {latency.Operation}: count={latency.Count} avg={latency.AverageMs:F1}ms p95={latency.P95Ms}ms max={latency.MaxMs}ms");
        }

        if (report.Inconsistencies.Count > 0)
        {
            Console.WriteLine("Inconsistencies:");
            foreach (var inconsistency in report.Inconsistencies)
            {
                Console.WriteLine($"  {inconsistency}");
            }
        }
    }

    public static async Task WriteJsonAsync(LoadBotReport report, string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine, cancellationToken);
    }
}
