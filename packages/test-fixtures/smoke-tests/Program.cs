using Divinity.GameGateway;
using Divinity.GameRules;
using Divinity.Launcher;
using Divinity.PlatformApi;
using Divinity.TestFixtures;
using Divinity.WorldRuntime;

var checks = new[]
{
    Check("launcher placeholder is scoped", LauncherInfo.ComponentName == "launcher" && !LauncherInfo.ImplementsLogin),
    Check("platform api placeholder is scoped", PlatformApiInfo.ComponentName == "platform-api" && !PlatformApiInfo.ImplementsDomainPersistence),
    Check("game gateway placeholder is scoped", GameGatewayInfo.ComponentName == "game-gateway" && !GameGatewayInfo.ImplementsWssHandshake),
    Check("world runtime placeholder is scoped", WorldRuntimeInfo.ComponentName == "world-runtime" && !WorldRuntimeInfo.ImplementsMovement && !WorldRuntimeInfo.ImplementsCombat),
    Check("game rules package is empty", GameRulesInfo.ComponentName == "game-rules" && !GameRulesInfo.ContainsGameplayRules),
    Check("test fixtures package is empty", TestFixturesInfo.ComponentName == "test-fixtures" && !TestFixturesInfo.ContainsGameplayFixtures)
};

var failures = checks.Where(check => !check.Passed).ToArray();

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

if (failures.Length > 0)
{
    Console.Error.WriteLine($"Smoke test failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-001 smoke tests passed.");
return 0;

static SmokeCheck Check(string name, bool passed) => new(name, passed);

internal readonly record struct SmokeCheck(string Name, bool Passed);
