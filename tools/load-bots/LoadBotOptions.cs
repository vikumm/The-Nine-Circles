using System.Globalization;

namespace Divinity.LoadBots;

public sealed record LoadBotOptions(
    string Profile,
    IReadOnlyList<int> RampSteps,
    int Iterations,
    int ConcurrentCombatBots,
    int ResilienceBots,
    int DisconnectPercent,
    int SlowClientDelayMs,
    bool RunReplayScenario,
    bool RunSlowClientScenario,
    bool RunGatewayRestartScenario,
    bool RunWorldShutdownScenario,
    bool RunHardeningScenario,
    bool RunProfilingScenario,
    int SoakDurationSeconds,
    string? ReportPath)
{
    public static LoadBotOptions Smoke() =>
        new(
            Profile: "smoke",
            RampSteps: [1],
            Iterations: 1,
            ConcurrentCombatBots: 1,
            ResilienceBots: 5,
            DisconnectPercent: 20,
            SlowClientDelayMs: 25,
            RunReplayScenario: true,
            RunSlowClientScenario: true,
            RunGatewayRestartScenario: true,
            RunWorldShutdownScenario: true,
            RunHardeningScenario: false,
            RunProfilingScenario: false,
            SoakDurationSeconds: 0,
            ReportPath: null);

    public static LoadBotOptions Acceptance() =>
        Smoke() with
        {
            Profile = "acceptance",
            RampSteps = [1, 25, 50, 100],
            ConcurrentCombatBots = 50,
            ResilienceBots = 100,
            SlowClientDelayMs = 100
        };

    public static LoadBotOptions Hardening() =>
        Smoke() with
        {
            Profile = "hardening",
            RampSteps = [1],
            ConcurrentCombatBots = 50,
            ResilienceBots = 100,
            SlowClientDelayMs = 50,
            RunHardeningScenario = true,
            RunProfilingScenario = true,
            SoakDurationSeconds = 1
        };

    public static LoadBotOptions Soak() =>
        Hardening() with
        {
            Profile = "soak",
            SoakDurationSeconds = 7200
        };

    public static LoadBotOptions Parse(string[] args)
    {
        var options = Smoke();

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            var value = index + 1 < args.Length ? args[index + 1] : string.Empty;

            switch (current)
            {
                case "--profile":
                    options = value.ToLowerInvariant() switch
                    {
                        "acceptance" => Acceptance(),
                        "hardening" => Hardening(),
                        "soak" => Soak(),
                        "smoke" => Smoke(),
                        _ => Smoke() with { Profile = string.IsNullOrWhiteSpace(value) ? "smoke" : value }
                    };
                    index++;
                    break;
                case "--ramp":
                    options = options with { RampSteps = ParseRamp(value) };
                    index++;
                    break;
                case "--iterations":
                    options = options with { Iterations = ParsePositive(value, current) };
                    index++;
                    break;
                case "--combat-bots":
                    options = options with { ConcurrentCombatBots = ParsePositive(value, current) };
                    index++;
                    break;
                case "--resilience-bots":
                    options = options with { ResilienceBots = ParsePositive(value, current) };
                    index++;
                    break;
                case "--disconnect-percent":
                    options = options with { DisconnectPercent = Math.Clamp(ParsePositive(value, current), 1, 100) };
                    index++;
                    break;
                case "--slow-client-delay-ms":
                    options = options with { SlowClientDelayMs = Math.Max(0, ParseNonNegative(value, current)) };
                    index++;
                    break;
                case "--report":
                    options = options with { ReportPath = value };
                    index++;
                    break;
                case "--soak-duration-seconds":
                    options = options with { SoakDurationSeconds = ParseNonNegative(value, current) };
                    index++;
                    break;
                case "--hardening":
                    options = options with { RunHardeningScenario = true };
                    break;
                case "--profiling":
                    options = options with { RunProfilingScenario = true };
                    break;
                case "--no-replay":
                    options = options with { RunReplayScenario = false };
                    break;
                case "--no-slow-client":
                    options = options with { RunSlowClientScenario = false };
                    break;
                case "--no-gateway-restart":
                    options = options with { RunGatewayRestartScenario = false };
                    break;
                case "--no-world-shutdown":
                    options = options with { RunWorldShutdownScenario = false };
                    break;
                case "--no-hardening":
                    options = options with { RunHardeningScenario = false };
                    break;
                case "--no-profiling":
                    options = options with { RunProfilingScenario = false };
                    break;
                case "--help":
                case "-h":
                    throw new LoadBotUsageException(CreateUsage());
                default:
                    throw new LoadBotUsageException($"Unknown argument '{current}'.{Environment.NewLine}{CreateUsage()}");
            }
        }

        return options;
    }

    public static string CreateUsage() =>
        """
        MMO-VS1 load bot and VS-020 gate

        Options:
          --profile smoke|acceptance|hardening|soak
          --ramp 1,25,50,100
          --iterations 1
          --combat-bots 50
          --resilience-bots 100
          --disconnect-percent 20
          --slow-client-delay-ms 100
          --hardening
          --profiling
          --soak-duration-seconds 7200
          --report path/to/report.json
        """;

    private static IReadOnlyList<int> ParseRamp(string value)
    {
        var steps = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .Where(step => step > 0)
            .ToArray();

        if (steps.Length == 0)
        {
            throw new LoadBotUsageException("--ramp requires at least one positive integer.");
        }

        return steps;
    }

    private static int ParsePositive(string value, string option)
    {
        var parsed = ParseNonNegative(value, option);
        if (parsed <= 0)
        {
            throw new LoadBotUsageException($"{option} must be greater than zero.");
        }

        return parsed;
    }

    private static int ParseNonNegative(string value, string option)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw new LoadBotUsageException($"{option} must be a non-negative integer.");
        }

        return parsed;
    }
}

public sealed class LoadBotUsageException : Exception
{
    public LoadBotUsageException(string message)
        : base(message)
    {
    }
}
