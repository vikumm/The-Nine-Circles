using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Combat;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;

var checks = new List<ShieldBashCheck>
{
    await CheckAsync("shield bash costs 10 MP on valid hit", MpCostAsync),
    await CheckAsync("shield bash enforces 5 second cooldown", CooldownAsync),
    await CheckAsync("shield bash applies and expires 1.25 second stun", StunDurationAsync),
    await CheckAsync("shield bash grants skill XP on valid hit", SkillXpValidHitAsync),
    await CheckAsync("shield bash grants no skill XP on error, empty action or dead target", NoSkillXpOnInvalidCastsAsync),
    await CheckAsync("shield bash action id is idempotent", IdempotentActionIdAsync),
    await CheckAsync("shield bash reaches rank 2 at 20 skill XP", RankTwoAtTwentyXpAsync),
    await CheckAsync("shield bash rejects invalid class", InvalidClassAsync),
    await CheckAsync("shield bash rejects no MP, out of range and stunned attacker", RejectsMpRangeAndStunAsync)
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-014 shield bash tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-014 shield bash tests passed.");
return 0;

static async Task<bool> MpCostAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var result = fixture.Cast(sequence: 1, actionId: "mp-cost");
    var resource = fixture.Combat.GetResourceState(fixture.Character.CharacterId);

    return result.Accepted
        && resource?.Mp == KnightCatalog.LevelOneStats.MaxMp - ShieldBashCatalog.ResourceCostMp
        && resource.MaxMp == KnightCatalog.LevelOneStats.MaxMp;
}

static async Task<bool> CooldownAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var first = fixture.Cast(sequence: 1, actionId: "cooldown-1");
    var second = fixture.Cast(sequence: 2, actionId: "cooldown-2");
    var state = fixture.Combat.GetSkillState(fixture.Character.CharacterId, ShieldBashCatalog.SkillId);

    return first.Accepted
        && second.Status == WorldShieldBashStatus.Cooldown
        && second.SkillStateChanged is not null
        && second.SkillStateChanged.SkillId == ShieldBashCatalog.SkillId
        && state.SkillXp == ShieldBashCatalog.SkillXpPerValidHit;
}

static async Task<bool> StunDurationAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var result = fixture.Cast(sequence: 1, actionId: "stun");
    var stunned = fixture.FirstSlime.IsStunned(fixture.Time.GetUtcNow());

    fixture.Time.Advance(ShieldBashCatalog.StunDuration);
    _ = fixture.Monsters.Tick(TimeSpan.Zero);
    var expired = !fixture.FirstSlime.IsStunned(fixture.Time.GetUtcNow());

    return result.Accepted
        && result.CombatEvent?.StatusEffects.SingleOrDefault()?.EffectId == ShieldBashCatalog.StunEffectId
        && result.CombatEvent.StatusEffects[0].DurationMs == (uint)ShieldBashCatalog.StunDuration.TotalMilliseconds
        && stunned
        && expired;
}

static async Task<bool> SkillXpValidHitAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var result = fixture.Cast(sequence: 1, actionId: "xp-valid");
    var state = fixture.Combat.GetSkillState(fixture.Character.CharacterId, ShieldBashCatalog.SkillId);

    return result.Accepted
        && result.CharacterProgressed is not null
        && result.CharacterProgressed.SkillXp == 1
        && result.CharacterProgressed.SkillRank == 1
        && state.SkillXp == 1
        && state.Rank == 1;
}

static async Task<bool> NoSkillXpOnInvalidCastsAsync()
{
    using var emptyAction = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var empty = emptyAction.Cast(sequence: 1, actionId: "");
    var emptyXp = emptyAction.Combat.GetSkillState(emptyAction.Character.CharacterId, ShieldBashCatalog.SkillId).SkillXp;

    using var outOfRange = await ShieldBashFixture.CreateAsync(spawnX: 40, spawnY: 48);
    var range = outOfRange.Cast(sequence: 1, actionId: "range-error");
    var rangeXp = outOfRange.Combat.GetSkillState(outOfRange.Character.CharacterId, ShieldBashCatalog.SkillId).SkillXp;

    using var deadTarget = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    _ = deadTarget.Monsters.KillMonster(deadTarget.TargetEntityId, deadTarget.Character.CharacterId);
    var dead = deadTarget.Cast(sequence: 1, actionId: "dead-target");
    var deadXp = deadTarget.Combat.GetSkillState(deadTarget.Character.CharacterId, ShieldBashCatalog.SkillId).SkillXp;

    return empty.Status == WorldShieldBashStatus.InvalidActionId
        && emptyXp == 0
        && range.Status == WorldShieldBashStatus.OutOfRange
        && rangeXp == 0
        && dead.Status == WorldShieldBashStatus.TargetDead
        && deadXp == 0;
}

static async Task<bool> IdempotentActionIdAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    var accepted = fixture.Cast(sequence: 1, actionId: "same-action");
    var duplicate = fixture.Cast(sequence: 2, actionId: "same-action");
    var state = fixture.Combat.GetSkillState(fixture.Character.CharacterId, ShieldBashCatalog.SkillId);

    return accepted.Accepted
        && duplicate.Status == WorldShieldBashStatus.DuplicateActionId
        && duplicate.CharacterProgressed is null
        && state.SkillXp == 1;
}

static async Task<bool> RankTwoAtTwentyXpAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    fixture.Combat.SetSkillXpForTesting(fixture.Character.CharacterId, ShieldBashCatalog.SkillId, ShieldBashCatalog.RankTwoRequiredXp - 1);
    var result = fixture.Cast(sequence: 1, actionId: "rank-two");
    var state = fixture.Combat.GetSkillState(fixture.Character.CharacterId, ShieldBashCatalog.SkillId);

    return result.Accepted
        && result.CharacterProgressed?.SkillXp == ShieldBashCatalog.RankTwoRequiredXp
        && result.CharacterProgressed.SkillRank == 2
        && result.CharacterProgressed.MaxRank == ShieldBashCatalog.MaxRank
        && state.Rank == 2;
}

static async Task<bool> InvalidClassAsync()
{
    using var fixture = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48, vocation: "Mage");
    var result = fixture.Cast(sequence: 1, actionId: "bad-class");

    return result.Status == WorldShieldBashStatus.InvalidClass
        && result.ErrorCode == ErrorCode.CastRejected
        && fixture.Combat.GetSkillState(fixture.Character.CharacterId, ShieldBashCatalog.SkillId).SkillXp == 0;
}

static async Task<bool> RejectsMpRangeAndStunAsync()
{
    using var noMp = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    noMp.Combat.SetResourceStateForTesting(noMp.Character.CharacterId, mp: 0);
    var noMpResult = noMp.Cast(sequence: 1, actionId: "no-mp");

    using var outOfRange = await ShieldBashFixture.CreateAsync(spawnX: 40, spawnY: 48);
    var outOfRangeResult = outOfRange.Cast(sequence: 1, actionId: "too-far");

    using var stunned = await ShieldBashFixture.CreateAsync(spawnX: 47, spawnY: 48);
    stunned.Movement.SetCharacterMotionState(stunned.Character.CharacterId, WorldCharacterMotionState.Stunned);
    var stunnedResult = stunned.Cast(sequence: 1, actionId: "stunned");

    return noMpResult.Status == WorldShieldBashStatus.InsufficientMp
        && noMp.Combat.GetResourceState(noMp.Character.CharacterId)?.Mp == 0
        && outOfRangeResult.Status == WorldShieldBashStatus.OutOfRange
        && stunnedResult.Status == WorldShieldBashStatus.AttackerStunned;
}

static async Task<ShieldBashCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new ShieldBashCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new ShieldBashCheck(name, false);
    }
}

internal readonly record struct ShieldBashCheck(string Name, bool Passed);

internal sealed class ShieldBashFixture : IDisposable
{
    private ShieldBashFixture(
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
    public WorldMonsterState FirstSlime => Monsters.Monsters.First();

    public static async Task<ShieldBashFixture> CreateAsync(int spawnX, int spawnY, string vocation = "Knight")
    {
        var storePath = CreateTempDirectory("vs014-shield-bash");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var map = await CreateMapCatalogAsync(spawnX, spawnY);
        var store = new FileCharacterStore(storePath);
        var character = string.Equals(vocation, KnightCatalog.Vocation, StringComparison.Ordinal)
            ? await CreateKnightAsync(store, time)
            : CreateManualCharacter(vocation, time.GetUtcNow());

        var movement = new WorldMovementRuntime(store, map, time);
        var monsters = new WorldMonsterRuntime(map, time);
        var combat = new WorldCombatRuntime(movement, monsters, time, new FixedCombatRandom(1.0d, critical: false));
        _ = await movement.JoinAsync(character, CancellationToken.None);
        return new ShieldBashFixture(storePath, character, movement, monsters, combat, time);
    }

    public WorldShieldBashResult Cast(ulong sequence, string actionId) =>
        Combat.ApplyShieldBash(
            Character.CharacterId,
            sequence,
            new CastIntent
            {
                SkillId = ShieldBashCatalog.SkillId,
                TargetEntityId = TargetEntityId,
                ActionId = actionId
            });

    public void Dispose() => DeleteDirectory(StorePath);

    private static async Task<CharacterRecord> CreateKnightAsync(FileCharacterStore store, ManualTimeProvider time)
    {
        var service = new CharacterService(store, time);
        var create = await service.CreateKnightAsync(new CreateKnightCommand("account-vs014", "Sir Shield"), CancellationToken.None);
        if (!create.Success || create.Character is null)
        {
            throw new InvalidOperationException(create.Message);
        }

        return create.Character;
    }

    private static CharacterRecord CreateManualCharacter(string vocation, DateTimeOffset now) =>
        new(
            "char-vs014-invalid-class",
            "account-vs014-invalid-class",
            "Sir Wrong",
            "sir wrong",
            vocation,
            KnightCatalog.CatalogVersion,
            KnightCatalog.LevelOneStats,
            KnightCatalog.MapId,
            KnightCatalog.ChannelId,
            KnightCatalog.ContentHash,
            KnightCatalog.SafeSpawn,
            now,
            now);

    private static async Task<WorldMapCatalog> CreateMapCatalogAsync(int safeSpawnX, int safeSpawnY)
    {
        var baseCatalog = await WorldMapCatalog.LoadDefaultAsync();
        var baseArtifact = baseCatalog.Artifact;
        var baseMap = baseArtifact.Map;
        var map = new MapDefinition
        {
            SchemaVersion = baseMap.SchemaVersion,
            MapId = baseMap.MapId,
            StableId = baseMap.StableId,
            Name = baseMap.Name,
            ContentVersion = baseMap.ContentVersion,
            Bounds = baseMap.Bounds,
            TileSize = baseMap.TileSize,
            BlockedCells = baseMap.BlockedCells,
            Regions = baseMap.Regions,
            Spawns = baseMap.Spawns,
            SafeSpawns = [new SafeSpawn { Id = "vs014-safe-spawn", X = safeSpawnX, Y = safeSpawnY }],
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
