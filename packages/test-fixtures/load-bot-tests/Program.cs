using Divinity.LoadBots;

var checks = new List<LoadBotFixtureCheck>
{
    await CheckAsync("flow of one bot", FlowOfOneBotAsync),
    Check("acceptance ramp is configurable", AcceptanceRampIsConfigurable()),
    await CheckAsync("load ramp executes", LoadRampExecutesAsync),
    await CheckAsync("replay message is rejected", ReplayMessageRejectedAsync),
    await CheckAsync("slow client completes", SlowClientCompletesAsync),
    await CheckAsync("batch disconnect reconnect", BatchDisconnectReconnectAsync),
    await CheckAsync("gateway restart during load", GatewayRestartDuringLoadAsync),
    await CheckAsync("world runtime graceful shutdown", WorldRuntimeGracefulShutdownAsync),
    await CheckAsync("duplicate reward count is zero", DuplicateRewardCountZeroAsync)
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-019 load bot tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-019 load bot tests passed.");
return 0;

static async Task<bool> FlowOfOneBotAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 1,
        ResilienceBots = 5,
        RunReplayScenario = false,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.BotResults.Count >= 2
        && report.BotResults.All(result => result.Success)
        && report.MessagesByType.ContainsKey("RewardGranted")
        && report.MessagesByType.ContainsKey("ReconnectRequest");
}

static bool AcceptanceRampIsConfigurable()
{
    var options = LoadBotOptions.Parse(["--profile", "acceptance"]);
    return options.RampSteps.SequenceEqual([1, 25, 50, 100])
        && options.ConcurrentCombatBots == 50
        && options.ResilienceBots == 100
        && options.DisconnectPercent == 20;
}

static async Task<bool> LoadRampExecutesAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1, 2],
        ConcurrentCombatBots = 1,
        ResilienceBots = 5,
        RunReplayScenario = false,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.Checks.Any(check => check.Name.StartsWith("ramp 1", StringComparison.Ordinal) && check.Passed)
        && report.Checks.Any(check => check.Name.StartsWith("ramp 2", StringComparison.Ordinal) && check.Passed);
}

static async Task<bool> ReplayMessageRejectedAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 1,
        ResilienceBots = 5,
        RunReplayScenario = true,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.BotResults.Any(result => result.BotId == "replay" && result.ReplayRejected)
        && report.MessagesByType.GetValueOrDefault("ServerError") > 0;
}

static async Task<bool> SlowClientCompletesAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 1,
        ResilienceBots = 5,
        SlowClientDelayMs = 10,
        RunReplayScenario = false,
        RunSlowClientScenario = true,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.Checks.Single(check => check.Name == "slow client completes flow").Passed;
}

static async Task<bool> BatchDisconnectReconnectAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 1,
        ResilienceBots = 10,
        DisconnectPercent = 20,
        RunReplayScenario = false,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.Checks.Single(check => check.Name == "20 percent disconnect and reconnect together").Passed;
}

static async Task<bool> GatewayRestartDuringLoadAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 1,
        ResilienceBots = 5,
        RunReplayScenario = false,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = true,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.Checks.Single(check => check.Name == "gateway restart during load preserves reconnect").Passed;
}

static async Task<bool> WorldRuntimeGracefulShutdownAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 1,
        ResilienceBots = 5,
        RunReplayScenario = false,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = true
    }, CancellationToken.None);

    return report.Success
        && report.Checks.Single(check => check.Name == "world runtime shutdown is graceful").Passed;
}

static async Task<bool> DuplicateRewardCountZeroAsync()
{
    var report = await new LoadBotRunner().RunAsync(LoadBotOptions.Smoke() with
    {
        RampSteps = [1],
        ConcurrentCombatBots = 50,
        ResilienceBots = 5,
        RunReplayScenario = true,
        RunSlowClientScenario = false,
        RunGatewayRestartScenario = false,
        RunWorldShutdownScenario = false
    }, CancellationToken.None);

    return report.Success
        && report.BotResults.Count(result => result.Success) >= 51
        && report.BotResults.Sum(result => result.DuplicateRewardRecords) == 0
        && report.Checks.Single(check => check.Name == "50 bots can fight simultaneously").Passed
        && report.Checks.Single(check => check.Name == "duplicated reward records equals zero").Passed;
}

static async Task<LoadBotFixtureCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new LoadBotFixtureCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new LoadBotFixtureCheck(name, false);
    }
}

static LoadBotFixtureCheck Check(string name, bool passed) => new(name, passed);

internal readonly record struct LoadBotFixtureCheck(string Name, bool Passed);
