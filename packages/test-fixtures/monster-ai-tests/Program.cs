using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.GameRules.Monsters;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Monsters;

var checks = new List<MonsterAiCheck>();

checks.Add(await CheckAsync("moss slime spawns inside valid combat region", SpawnInsideValidRegionAsync));
checks.Add(await CheckAsync("moss slime aggro detects player within 6 units", AggroProximityAsync));
checks.Add(await CheckAsync("moss slime leash enters Return and regenerates without reward", LeashReturnAsync));
checks.Add(await CheckAsync("moss slime path recalculation is capped at 4 per second", PathRecalculationLimitAsync));
checks.Add(await CheckAsync("moss slime loses dead target", TargetLossDeadAsync));
checks.Add(await CheckAsync("moss slime loses disconnected target", TargetLossDisconnectedAsync));
checks.Add(await CheckAsync("moss slime respawns 8 seconds after valid death", RespawnAfterEightSecondsAsync));
checks.Add(await CheckAsync("moss slime emits preliminary attack event in range", SimpleAttackEventAsync));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-012 monster AI tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-012 monster AI tests passed.");
return 0;

static async Task<bool> SpawnInsideValidRegionAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var monsters = fixture.Runtime.Monsters;

    return monsters.Count > 0
        && monsters.Count <= MossSlimeCatalog.MaxActiveInstances
        && fixture.Runtime.ActiveMonsterCount == monsters.Count
        && monsters.All(monster => monster.TemplateId == MossSlimeCatalog.TemplateId)
        && monsters.All(monster => fixture.Map.IsNavigable(monster.X, monster.Y))
        && monsters.All(monster => fixture.Map.IsInsideRegionKind(RegionKind.CombatZone, monster.X, monster.Y))
        && fixture.Runtime.Tick(TimeSpan.Zero).Snapshot.Entities.Count(entity => entity.Kind == EntityKind.Monster) == monsters.Count;
}

static async Task<bool> AggroProximityAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    fixture.Runtime.UpsertPlayer("player-aggro", slime.SpawnX + 5d, slime.SpawnY);

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    var result = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    var updated = fixture.FirstSlime;

    return updated.AiState == MonsterAiState.Chase
        && updated.TargetCharacterId == "player-aggro"
        && result.Snapshot.Entities.Any(entity => entity.EntityId == updated.EntityId && entity.Kind == EntityKind.Monster);
}

static async Task<bool> LeashReturnAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    fixture.Runtime.UpsertPlayer("player-leash", slime.SpawnX + 5d, slime.SpawnY);

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    _ = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    fixture.Runtime.UpsertPlayer("player-leash", slime.SpawnX + MossSlimeCatalog.Level1.LeashRadius + 1d, slime.SpawnY);

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    var returned = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    var updated = fixture.FirstSlime;

    return updated.AiState == MonsterAiState.Return
        && updated.TargetCharacterId is null
        && updated.Hp == MossSlimeCatalog.Level1.MaxHp
        && returned.RewardEvents.Count == 0;
}

static async Task<bool> PathRecalculationLimitAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    fixture.Runtime.UpsertPlayer("player-path", slime.SpawnX + 5d, slime.SpawnY + 0.5d);

    for (var tick = 0; tick < 10; tick++)
    {
        fixture.Time.Advance(TimeSpan.FromMilliseconds(100));
        _ = fixture.Runtime.Tick(TimeSpan.FromMilliseconds(100));
    }

    return fixture.FirstSlime.PathRecalculationCount <= 4;
}

static async Task<bool> TargetLossDeadAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    fixture.Runtime.UpsertPlayer("player-dead", slime.SpawnX + 5d, slime.SpawnY);

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    _ = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    fixture.Runtime.SetPlayerAlive("player-dead", alive: false);

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    _ = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    var updated = fixture.FirstSlime;

    return updated.AiState == MonsterAiState.Return
        && updated.TargetCharacterId is null;
}

static async Task<bool> TargetLossDisconnectedAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    fixture.Runtime.UpsertPlayer("player-disconnect", slime.SpawnX + 5d, slime.SpawnY);

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    _ = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    fixture.Runtime.DisconnectPlayer("player-disconnect");

    fixture.Time.Advance(MossSlimeCatalog.PathRecalculationInterval);
    _ = fixture.Runtime.Tick(MossSlimeCatalog.PathRecalculationInterval);
    var updated = fixture.FirstSlime;

    return updated.AiState == MonsterAiState.Return
        && updated.TargetCharacterId is null;
}

static async Task<bool> RespawnAfterEightSecondsAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    var death = fixture.Runtime.KillMonster(slime.EntityId, "player-killer");
    var deadSnapshot = fixture.Runtime.Tick(TimeSpan.Zero).Snapshot;

    fixture.Time.Advance(MossSlimeCatalog.Level1.RespawnDelay - TimeSpan.FromMilliseconds(1));
    _ = fixture.Runtime.Tick(TimeSpan.FromMilliseconds(1));
    var beforeRespawn = fixture.FirstSlime;

    fixture.Time.Advance(TimeSpan.FromMilliseconds(1));
    var respawn = fixture.Runtime.Tick(TimeSpan.FromMilliseconds(1));
    var afterRespawn = fixture.FirstSlime;

    return death.Success
        && death.KillId is not null
        && deadSnapshot.Entities.All(entity => entity.EntityId != slime.EntityId)
        && beforeRespawn.AiState == MonsterAiState.Dead
        && afterRespawn.AiState == MonsterAiState.Idle
        && afterRespawn.Hp == MossSlimeCatalog.Level1.MaxHp
        && Near(afterRespawn.X, slime.SpawnX)
        && Near(afterRespawn.Y, slime.SpawnY)
        && respawn.Snapshot.Entities.Any(entity => entity.EntityId == slime.EntityId);
}

static async Task<bool> SimpleAttackEventAsync()
{
    using var fixture = await MonsterAiFixture.CreateAsync();
    var slime = fixture.FirstSlime;
    fixture.Runtime.UpsertPlayer("player-attack", slime.SpawnX + 0.5d, slime.SpawnY, hp: 30);

    var result = fixture.Runtime.Tick(TimeSpan.Zero);
    var updated = fixture.FirstSlime;
    var combat = result.CombatEvents.SingleOrDefault();

    return updated.AiState == MonsterAiState.Attack
        && combat is not null
        && combat.SourceEntityId == slime.EntityId
        && combat.TargetEntityId == "player-attack"
        && combat.SkillId == MossSlimeCatalog.BasicAttackSkillId
        && combat.Result == CombatResult.Hit
        && combat.Damage == MossSlimeCatalog.Level1.Attack
        && combat.TargetHp == 24
        && result.RewardEvents.Count == 0;
}

static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.000001d;

static async Task<MonsterAiCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new MonsterAiCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new MonsterAiCheck(name, false);
    }
}

internal readonly record struct MonsterAiCheck(string Name, bool Passed);

internal sealed class MonsterAiFixture : IDisposable
{
    private MonsterAiFixture(WorldMapCatalog map, WorldMonsterRuntime runtime, ManualTimeProvider time)
    {
        Map = map;
        Runtime = runtime;
        Time = time;
    }

    public WorldMapCatalog Map { get; }
    public WorldMonsterRuntime Runtime { get; }
    public ManualTimeProvider Time { get; }
    public WorldMonsterState FirstSlime => Runtime.Monsters.First(monster => monster.TemplateId == MossSlimeCatalog.TemplateId);

    public static async Task<MonsterAiFixture> CreateAsync()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var map = await WorldMapCatalog.LoadDefaultAsync();
        var runtime = new WorldMonsterRuntime(map, time);
        return new MonsterAiFixture(map, runtime, time);
    }

    public void Dispose()
    {
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan value) => _utcNow += value;
}
