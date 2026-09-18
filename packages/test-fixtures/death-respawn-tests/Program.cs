using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Combat;
using Divinity.GameRules.Equipment;
using Divinity.GameRules.Monsters;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Equipment;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;

var checks = new List<DeathRespawnCheck>
{
    await CheckAsync("character HP zero enters Dead state", CharacterDeathAsync),
    await CheckAsync("death screen lasts 5 seconds", DeathScreenDurationAsync),
    await CheckAsync("respawn occurs at Safe Spawn", RespawnAtSafeSpawnAsync),
    await CheckAsync("safe checkpoint is stored after respawn", SafeCheckpointAsync),
    await CheckAsync("durable equipment loses 1 durability on death", DurabilityLossAsync),
    await CheckAsync("zero durability equipment stays equipped without attributes", ZeroDurabilityNoAttributesAsync),
    await CheckAsync("monster death emits unique kill_id", MonsterKillIdAsync),
    await CheckAsync("delayed attack against dead monster does not duplicate death or skill XP", DeadMonsterNoDuplicateAsync),
    await CheckAsync("disconnect during death does not duplicate actor", DisconnectDuringDeathAsync)
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-015 death/respawn tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-015 death/respawn tests passed.");
return 0;

static async Task<bool> CharacterDeathAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    var result = fixture.KillCharacter();
    var actor = fixture.Movement.GetCombatActor(fixture.Character.CharacterId);
    var entity = result.Snapshot?.Entities.SingleOrDefault(entity => entity.EntityId == fixture.Character.CharacterId);

    return result.Status == WorldCharacterDamageStatus.Killed
        && actor?.State == WorldCombatActorState.Dead
        && result.TargetHp == 0
        && result.CombatEvent?.TargetHp == 0
        && entity?.Hp == 0;
}

static async Task<bool> DeathScreenDurationAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    var start = fixture.Time.GetUtcNow();
    var result = fixture.KillCharacter();
    var effect = result.CombatEvent?.StatusEffects.SingleOrDefault();

    return result.RespawnAtUtc == start + CharacterLifeCatalog.DeathScreenDuration
        && effect?.EffectId == CharacterLifeCatalog.DeathEffectId
        && effect.DurationMs == 5000;
}

static async Task<bool> RespawnAtSafeSpawnAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    await fixture.MoveAwayFromSafeSpawnAsync();
    _ = fixture.KillCharacter();

    fixture.Time.Advance(CharacterLifeCatalog.DeathScreenDuration - TimeSpan.FromMilliseconds(1));
    var early = await fixture.Movement.RespawnDueCharactersAsync(CancellationToken.None);
    var stillDead = fixture.Movement.GetCombatActor(fixture.Character.CharacterId)?.State == WorldCombatActorState.Dead;

    fixture.Time.Advance(TimeSpan.FromMilliseconds(1));
    var respawns = await fixture.Movement.RespawnDueCharactersAsync(CancellationToken.None);
    var respawn = respawns.SingleOrDefault();
    var actor = fixture.Movement.GetCombatActor(fixture.Character.CharacterId);

    return early.Count == 0
        && stillDead
        && respawn is not null
        && respawn.Position == fixture.SafeSpawn
        && actor?.State == WorldCombatActorState.Alive
        && respawn.Stats.Hp == KnightCatalog.LevelOneStats.MaxHp
        && respawn.Stats.Mp == KnightCatalog.LevelOneStats.MaxMp;
}

static async Task<bool> SafeCheckpointAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    await fixture.MoveAwayFromSafeSpawnAsync();
    _ = fixture.KillCharacter();

    fixture.Time.Advance(CharacterLifeCatalog.DeathScreenDuration);
    var respawns = await fixture.Movement.RespawnDueCharactersAsync(CancellationToken.None);
    var checkpoint = await fixture.Store.GetCheckpointAsync(fixture.Character.CharacterId, CancellationToken.None);

    return respawns.SingleOrDefault()?.CheckpointStored == true
        && checkpoint is not null
        && checkpoint.Reason == "respawn"
        && checkpoint.Position == fixture.SafeSpawn;
}

static async Task<bool> DurabilityLossAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    fixture.Equipment.EquipWoodenShieldForTesting(
        fixture.Character.CharacterId,
        "item:shield:durability-loss",
        EquipmentRarity.Normal,
        WoodenShieldCatalog.MaxDurability);

    var result = fixture.KillCharacter();
    var equipped = fixture.Equipment.GetEquipment(fixture.Character.CharacterId).Single();
    var delta = result.InventoryDelta?.Equipment.SingleOrDefault();

    return equipped.Durability == WoodenShieldCatalog.MaxDurability - 1
        && delta is not null
        && delta.Durability == WoodenShieldCatalog.MaxDurability - 1
        && delta.MaxDurability == WoodenShieldCatalog.MaxDurability
        && delta.AttributesActive;
}

static async Task<bool> ZeroDurabilityNoAttributesAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    fixture.Equipment.EquipWoodenShieldForTesting(
        fixture.Character.CharacterId,
        "item:shield:zero-durability",
        EquipmentRarity.Rare,
        durability: 0);

    var actor = fixture.Movement.GetCombatActor(fixture.Character.CharacterId);
    var equipped = fixture.Equipment.GetEquipment(fixture.Character.CharacterId).Single();

    return actor?.Stats.Defense == KnightCatalog.LevelOneStats.Defense
        && equipped.Durability == 0
        && !equipped.AttributesActive;
}

static async Task<bool> MonsterKillIdAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    var target = fixture.TargetEntityId;
    _ = fixture.Monsters.ApplyDamageToMonster(target, MossSlimeCatalog.Level1.MaxHp - 1, fixture.Character.CharacterId);
    var kill = fixture.Attack(sequence: 1, targetEntityId: target);

    fixture.Time.Advance(MossSlimeCatalog.Level1.RespawnDelay);
    _ = fixture.Monsters.Tick(MossSlimeCatalog.Level1.RespawnDelay);
    var secondKill = fixture.Monsters.ApplyDamageToMonster(target, MossSlimeCatalog.Level1.MaxHp, fixture.Character.CharacterId);

    return kill.Accepted
        && !string.IsNullOrWhiteSpace(kill.CombatEvent?.KillId)
        && secondKill.Success
        && !string.IsNullOrWhiteSpace(secondKill.KillId)
        && kill.CombatEvent.KillId != secondKill.KillId;
}

static async Task<bool> DeadMonsterNoDuplicateAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    var target = fixture.TargetEntityId;
    _ = fixture.Monsters.ApplyDamageToMonster(target, MossSlimeCatalog.Level1.MaxHp - 1, fixture.Character.CharacterId);
    var kill = fixture.Attack(sequence: 1, targetEntityId: target);

    var lateBasicAttack = fixture.Attack(sequence: 2, targetEntityId: target);
    var lateShieldBash = fixture.Cast(sequence: 3, actionId: "late-shield-bash", targetEntityId: target);
    var skill = fixture.Combat.GetSkillState(fixture.Character.CharacterId, ShieldBashCatalog.SkillId);
    var deadTarget = fixture.Monsters.ApplyDamageToMonster(target, 999, fixture.Character.CharacterId);

    return kill.Accepted
        && lateBasicAttack.Status == WorldBasicAttackStatus.TargetDead
        && lateShieldBash.Status == WorldShieldBashStatus.TargetDead
        && lateShieldBash.CharacterProgressed is null
        && skill.SkillXp == 0
        && !deadTarget.Success
        && deadTarget.KillId == kill.CombatEvent?.KillId;
}

static async Task<bool> DisconnectDuringDeathAsync()
{
    using var fixture = await DeathRespawnFixture.CreateAsync();
    fixture.Equipment.EquipWoodenShieldForTesting(
        fixture.Character.CharacterId,
        "item:shield:disconnect",
        EquipmentRarity.Normal,
        durability: 20);
    _ = fixture.KillCharacter();

    await fixture.Movement.DisconnectAsync(fixture.Character.CharacterId, "test-disconnect-during-death", CancellationToken.None);
    var countAfterDisconnect = fixture.Movement.ActiveActorCount;

    _ = await fixture.Movement.JoinAsync(fixture.Character, CancellationToken.None);
    _ = await fixture.Movement.JoinAsync(fixture.Character, CancellationToken.None);
    var actor = fixture.Movement.GetCombatActor(fixture.Character.CharacterId);
    var equipped = fixture.Equipment.GetEquipment(fixture.Character.CharacterId).Single();

    return countAfterDisconnect == 0
        && fixture.Movement.ActiveActorCount == 1
        && actor?.State == WorldCombatActorState.Dead
        && equipped.Durability == 19;
}

static async Task<DeathRespawnCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new DeathRespawnCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new DeathRespawnCheck(name, false);
    }
}

internal readonly record struct DeathRespawnCheck(string Name, bool Passed);

internal sealed class DeathRespawnFixture : IDisposable
{
    private DeathRespawnFixture(
        string storePath,
        CharacterRecord character,
        FileCharacterStore store,
        WorldEquipmentRuntime equipment,
        WorldMovementRuntime movement,
        WorldMonsterRuntime monsters,
        WorldCombatRuntime combat,
        ManualTimeProvider time,
        CharacterPosition safeSpawn)
    {
        StorePath = storePath;
        Character = character;
        Store = store;
        Equipment = equipment;
        Movement = movement;
        Monsters = monsters;
        Combat = combat;
        Time = time;
        SafeSpawn = safeSpawn;
    }

    public string StorePath { get; }
    public CharacterRecord Character { get; }
    public FileCharacterStore Store { get; }
    public WorldEquipmentRuntime Equipment { get; }
    public WorldMovementRuntime Movement { get; }
    public WorldMonsterRuntime Monsters { get; }
    public WorldCombatRuntime Combat { get; }
    public ManualTimeProvider Time { get; }
    public CharacterPosition SafeSpawn { get; }
    public string TargetEntityId => Monsters.Monsters.First().EntityId;

    public static async Task<DeathRespawnFixture> CreateAsync()
    {
        var storePath = CreateTempDirectory("vs015-death-respawn");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var safeSpawn = new CharacterPosition(47, 48);
        var map = await CreateMapCatalogAsync(safeSpawn);
        var store = new FileCharacterStore(storePath);
        var service = new CharacterService(store, time);
        var create = await service.CreateKnightAsync(new CreateKnightCommand("account-vs015", "Sir Respawn"), CancellationToken.None);
        if (!create.Success || create.Character is null)
        {
            throw new InvalidOperationException(create.Message);
        }

        var equipment = new WorldEquipmentRuntime();
        var movement = new WorldMovementRuntime(store, map, time, equipment);
        var monsters = new WorldMonsterRuntime(map, time, movement);
        var combat = new WorldCombatRuntime(movement, monsters, time, new FixedCombatRandom(1.0d, critical: false));
        _ = await movement.JoinAsync(create.Character, CancellationToken.None);

        return new DeathRespawnFixture(storePath, create.Character, store, equipment, movement, monsters, combat, time, safeSpawn);
    }

    public WorldCharacterDamageResult KillCharacter() =>
        Movement.ApplyDamageToCharacter(
            Character.CharacterId,
            KnightCatalog.LevelOneStats.MaxHp,
            "monster:moss-slime-test",
            MossSlimeCatalog.BasicAttackSkillId);

    public WorldBasicAttackResult Attack(ulong sequence, string targetEntityId) =>
        Combat.ApplyBasicAttack(
            Character.CharacterId,
            sequence,
            new AttackIntent
            {
                TargetEntityId = targetEntityId,
                SkillId = BasicSlashCatalog.SkillId,
                ActionId = $"attack-{sequence}"
            });

    public WorldShieldBashResult Cast(ulong sequence, string actionId, string targetEntityId) =>
        Combat.ApplyShieldBash(
            Character.CharacterId,
            sequence,
            new CastIntent
            {
                SkillId = ShieldBashCatalog.SkillId,
                TargetEntityId = targetEntityId,
                ActionId = actionId
            });

    public async Task MoveAwayFromSafeSpawnAsync()
    {
        var move = await Movement.ApplyMoveAsync(
            Character.CharacterId,
            sequence: 1,
            clientTick: 1,
            new MoveIntent
            {
                Mode = MovementMode.Direction,
                DirectionX = 1,
                DirectionY = 0
            },
            CancellationToken.None);

        if (!move.Accepted)
        {
            throw new InvalidOperationException($"Could not move fixture character: {move.Message}");
        }
    }

    public void Dispose() => DeleteDirectory(StorePath);

    private static async Task<WorldMapCatalog> CreateMapCatalogAsync(CharacterPosition safeSpawn)
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
            SafeSpawns = [new SafeSpawn { Id = "vs015-safe-spawn", X = (int)safeSpawn.X, Y = (int)safeSpawn.Y }],
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

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
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
