using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Combat;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;

var checks = new List<BasicAttackCheck>
{
    Check("basic slash damage uses attack coefficient and base power", DamageCalculation()),
    Check("basic slash mitigation uses target defense", DefenseMitigation()),
    Check("basic slash variance is server controlled and testable", ControlledVariance()),
    Check("basic slash critical applies server roll", CriticalRoll()),
    Check("basic slash minimum damage is 1", MinimumDamage()),
    await CheckAsync("basic slash accepts target inside 1.5 range", RangeValidAsync),
    await CheckAsync("basic slash rejects target outside 1.5 range", RangeInvalidAsync),
    await CheckAsync("basic slash enforces server-side cooldown", CooldownAsync),
    await CheckAsync("basic slash rejects dead target", DeadTargetAsync),
    await CheckAsync("basic slash rejects missing target", MissingTargetAsync),
    await CheckAsync("basic slash rejects target on another map", OtherMapTargetAsync),
    await CheckAsync("basic slash rejects repeated sequence", RepeatedSequenceAsync),
    await CheckAsync("basic slash rejects stunned attacker", StunnedAttackerAsync)
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-013 basic attack tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-013 basic attack tests passed.");
return 0;

static bool DamageCalculation()
{
    var roll = BasicSlashDamageCalculator.Calculate(KnightCatalog.LevelOneStats, targetDefense: 0, new FixedCombatRandom(1.0d, critical: false));
    return roll.RawDamage == 14
        && roll.Damage == 14
        && !roll.Critical;
}

static bool DefenseMitigation()
{
    var roll = BasicSlashDamageCalculator.Calculate(KnightCatalog.LevelOneStats, targetDefense: 100, new FixedCombatRandom(1.0d, critical: false));
    return roll.Mitigation == 0.5m
        && roll.Damage == 7;
}

static bool ControlledVariance()
{
    var roll = BasicSlashDamageCalculator.Calculate(KnightCatalog.LevelOneStats, targetDefense: 0, new FixedCombatRandom(0.95d, critical: false));
    return roll.Variance == 0.95d
        && roll.Damage == 13;
}

static bool CriticalRoll()
{
    var roll = BasicSlashDamageCalculator.Calculate(KnightCatalog.LevelOneStats, targetDefense: 0, new FixedCombatRandom(1.0d, critical: true));
    return roll.Critical
        && roll.Damage == 21;
}

static bool MinimumDamage()
{
    var roll = BasicSlashDamageCalculator.Calculate(KnightCatalog.LevelOneStats, targetDefense: 100000, new FixedCombatRandom(0.95d, critical: false));
    return roll.Damage == 1;
}

static async Task<bool> RangeValidAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var result = fixture.Attack(sequence: 1);

    return result.Accepted
        && result.CombatEvent is not null
        && result.CombatEvent.SkillId == BasicSlashCatalog.SkillId
        && result.CombatEvent.Result == CombatResult.Hit
        && result.CombatEvent.Damage == 13
        && result.CombatEvent.TargetHp == 32;
}

static async Task<bool> RangeInvalidAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 40, spawnY: 48);
    var result = fixture.Attack(sequence: 1);

    return result.Status == WorldBasicAttackStatus.OutOfRange
        && result.ErrorCode == ErrorCode.AttackRejected
        && result.CombatEvent is null;
}

static async Task<bool> CooldownAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var accepted = fixture.Attack(sequence: 1);
    var cooldown = fixture.Attack(sequence: 2);

    return accepted.Accepted
        && cooldown.Status == WorldBasicAttackStatus.Cooldown
        && cooldown.SkillStateChanged is not null
        && cooldown.SkillStateChanged.SkillId == BasicSlashCatalog.SkillId
        && !cooldown.SkillStateChanged.Available;
}

static async Task<bool> DeadTargetAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 47, spawnY: 48);
    _ = fixture.Monsters.KillMonster(fixture.TargetEntityId, fixture.Character.CharacterId);
    var result = fixture.Attack(sequence: 1);

    return result.Status == WorldBasicAttackStatus.TargetDead
        && result.ErrorCode == ErrorCode.AttackRejected;
}

static async Task<bool> MissingTargetAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var result = fixture.Attack(sequence: 1, targetEntityId: "monster:missing");

    return result.Status == WorldBasicAttackStatus.TargetNotFound
        && result.ErrorCode == ErrorCode.AttackRejected;
}

static async Task<bool> OtherMapTargetAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(
        spawnX: 47,
        spawnY: 48,
        movementMapId: "training-field-01",
        monsterMapId: "other-map");
    var result = fixture.Attack(sequence: 1);

    return result.Status == WorldBasicAttackStatus.DifferentMap
        && result.ErrorCode == ErrorCode.AttackRejected;
}

static async Task<bool> RepeatedSequenceAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var accepted = fixture.Attack(sequence: 1);
    var repeated = fixture.Attack(sequence: 1);

    return accepted.Accepted
        && repeated.Status == WorldBasicAttackStatus.SequenceReplay
        && repeated.ErrorCode == ErrorCode.AttackRejected;
}

static async Task<bool> StunnedAttackerAsync()
{
    using var fixture = await BasicAttackFixture.CreateAsync(spawnX: 47, spawnY: 48);
    fixture.Movement.SetCharacterMotionState(fixture.Character.CharacterId, WorldCharacterMotionState.Stunned);
    var result = fixture.Attack(sequence: 1);

    return result.Status == WorldBasicAttackStatus.AttackerStunned
        && result.ErrorCode == ErrorCode.AttackRejected;
}

static BasicAttackCheck Check(string name, bool passed) => new(name, passed);

static async Task<BasicAttackCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new BasicAttackCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new BasicAttackCheck(name, false);
    }
}

internal readonly record struct BasicAttackCheck(string Name, bool Passed);

internal sealed class BasicAttackFixture : IDisposable
{
    private BasicAttackFixture(
        string storePath,
        CharacterRecord character,
        WorldMovementRuntime movement,
        WorldMonsterRuntime monsters,
        WorldCombatRuntime combat,
        ManualTimeProvider time)
    {
        StorePath = storePath;
        Character = character;
        Movement = movement;
        Monsters = monsters;
        Combat = combat;
        Time = time;
    }

    public string StorePath { get; }
    public CharacterRecord Character { get; }
    public WorldMovementRuntime Movement { get; }
    public WorldMonsterRuntime Monsters { get; }
    public WorldCombatRuntime Combat { get; }
    public ManualTimeProvider Time { get; }
    public string TargetEntityId => Monsters.Monsters.First().EntityId;

    public static async Task<BasicAttackFixture> CreateAsync(
        int spawnX,
        int spawnY,
        string movementMapId = "training-field-01",
        string monsterMapId = "training-field-01",
        ICombatRandomSource? random = null)
    {
        var storePath = CreateTempDirectory("vs013-basic-attack");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var movementCatalog = await CreateMapCatalogAsync(movementMapId, spawnX, spawnY);
        var monsterCatalog = await CreateMapCatalogAsync(monsterMapId, spawnX, spawnY);
        var store = new FileCharacterStore(storePath);
        var characters = new CharacterService(store, time);
        var create = await characters.CreateKnightAsync(new CreateKnightCommand("account-vs013", "Sir Slash"), CancellationToken.None);
        if (!create.Success || create.Character is null)
        {
            throw new InvalidOperationException(create.Message);
        }

        var movement = new WorldMovementRuntime(store, movementCatalog, time);
        var monsters = new WorldMonsterRuntime(monsterCatalog, time);
        var combat = new WorldCombatRuntime(movement, monsters, time, random ?? new FixedCombatRandom(1.0d, critical: false));
        _ = await movement.JoinAsync(create.Character, CancellationToken.None);
        return new BasicAttackFixture(storePath, create.Character, movement, monsters, combat, time);
    }

    public WorldBasicAttackResult Attack(ulong sequence, string? targetEntityId = null) =>
        Combat.ApplyBasicAttack(
            Character.CharacterId,
            sequence,
            new AttackIntent
            {
                TargetEntityId = targetEntityId ?? TargetEntityId,
                SkillId = BasicSlashCatalog.SkillId,
                ActionId = $"action-{sequence}"
            });

    public void Dispose() => DeleteDirectory(StorePath);

    private static async Task<WorldMapCatalog> CreateMapCatalogAsync(string mapId, int safeSpawnX, int safeSpawnY)
    {
        var baseCatalog = await WorldMapCatalog.LoadDefaultAsync();
        var baseArtifact = baseCatalog.Artifact;
        var baseMap = baseArtifact.Map;
        var map = new MapDefinition
        {
            SchemaVersion = baseMap.SchemaVersion,
            MapId = mapId,
            StableId = baseMap.StableId,
            Name = baseMap.Name,
            ContentVersion = baseMap.ContentVersion,
            Bounds = baseMap.Bounds,
            TileSize = baseMap.TileSize,
            BlockedCells = baseMap.BlockedCells,
            Regions = baseMap.Regions,
            Spawns = baseMap.Spawns,
            SafeSpawns = [new SafeSpawn { Id = "vs013-safe-spawn", X = safeSpawnX, Y = safeSpawnY }],
            Triggers = baseMap.Triggers
        };

        return WorldMapCatalog.FromArtifact(new ServerContentArtifact
        {
            SchemaVersion = baseArtifact.SchemaVersion,
            ContentVersion = baseArtifact.ContentVersion,
            ContentHash = baseArtifact.ContentHash,
            Map = map,
            Chunks = baseArtifact.Chunks,
            Skills = baseArtifact.Skills,
            Items = baseArtifact.Items,
            LootTables = baseArtifact.LootTables
        });
    }

    private static string CreateTempDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "divinity", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

internal sealed class FixedCombatRandom : ICombatRandomSource
{
    private readonly double _variance;
    private readonly bool _critical;

    public FixedCombatRandom(double variance, bool critical)
    {
        _variance = variance;
        _critical = critical;
    }

    public double NextVariance() => _variance;

    public bool RollCritical(decimal criticalChancePercent) => _critical;
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
