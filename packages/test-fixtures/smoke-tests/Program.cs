using Divinity.GameGateway;
using Divinity.GameRules;
using Divinity.Launcher;
using Divinity.PlatformApi;
using Divinity.TestFixtures;
using Divinity.WorldRuntime;

var checks = new[]
{
    Check("launcher placeholder is scoped", LauncherInfo.ComponentName == "launcher" && !LauncherInfo.ImplementsLogin),
    Check("platform api character gate is scoped", PlatformApiInfo.ComponentName == "platform-api" && PlatformApiInfo.ImplementsDomainPersistence && PlatformApiInfo.CreatesKnight),
    Check("game gateway combat gate is scoped", GameGatewayInfo.ComponentName == "game-gateway" && GameGatewayInfo.ImplementsWssHandshake && GameGatewayInfo.RoutesGameplayIntents && GameGatewayInfo.RoutesAttackIntents),
    Check("world runtime combat gate is scoped", WorldRuntimeInfo.ComponentName == "world-runtime" && WorldRuntimeInfo.UsesAuthoritativeMapArtifact && WorldRuntimeInfo.ImplementsMovement && WorldRuntimeInfo.ImplementsMonsterAi && WorldRuntimeInfo.ImplementsCombat),
    Check("game rules package has VS-013 combat rules", GameRulesInfo.ComponentName == "game-rules" && GameRulesInfo.ContainsBalanceData && GameRulesInfo.ContainsGameplayRules && GameRulesInfo.ContainsMovementRules && GameRulesInfo.ContainsPredictionRules && GameRulesInfo.ContainsMonsterRules && GameRulesInfo.ContainsCombatRules),
    Check("test fixtures package has VS-013 combat fixtures", TestFixturesInfo.ComponentName == "test-fixtures" && TestFixturesInfo.ContainsGameplayFixtures && TestFixturesInfo.ContainsMonsterAiFixtures && TestFixturesInfo.ContainsCombatFixtures)
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

Console.WriteLine("VS-001/VS-013 smoke tests passed.");
return 0;

static SmokeCheck Check(string name, bool passed) => new(name, passed);

internal readonly record struct SmokeCheck(string Name, bool Passed);
