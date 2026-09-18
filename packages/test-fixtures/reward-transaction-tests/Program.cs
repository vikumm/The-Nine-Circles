using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Combat;
using Divinity.GameRules.Monsters;
using Divinity.GameRules.Rewards;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;
using Divinity.WorldRuntime.Rewards;

var checks = new List<RewardTransactionCheck>
{
    await CheckAsync("valid monster death grants reward transaction", ValidRewardGrantAsync),
    Check("reward_key idempotency blocks duplicate grant", RewardKeyIdempotency()),
    Check("transient failure retries safely", TransientRetry()),
    Check("deadlock retry is safe", DeadlockRetry()),
    Check("full inventory sends item to pending_reward and grants currency", FullInventoryPendingReward()),
    Check("currency ledger is append-only", LedgerAppendOnly()),
    Check("outbox event exists only with committed reward", OutboxAfterCommit()),
    Check("deterministic QA loot table covers potion and shield rarities", DeterministicQaLootTable())
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-016 reward transaction tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-016 reward transaction tests passed.");
return 0;

static async Task<bool> ValidRewardGrantAsync()
{
    using var fixture = await RewardCombatFixture.CreateAsync(new FixedRewardRandom([2], [100]));
    var target = fixture.TargetEntityId;
    _ = fixture.Monsters.ApplyDamageToMonster(target, MossSlimeCatalog.Level1.MaxHp - 13, fixture.Character.CharacterId);

    var result = fixture.Attack(sequence: 1, targetEntityId: target);
    var document = fixture.Rewards.ReadDocumentForTesting();
    var rewardKey = $"{result.CombatEvent?.KillId}:{fixture.Character.CharacterId}";

    return result.Accepted
        && !string.IsNullOrWhiteSpace(result.CombatEvent?.KillId)
        && result.RewardGranted is not null
        && result.RewardGranted.RewardKey == rewardKey
        && result.RewardGranted.Xp == MossSlimeRewardCatalog.KillXp
        && result.RewardGranted.CurrencyDelta == 2
        && result.RewardGranted.ItemInstanceIds.Count == 1
        && result.InventoryDelta?.CurrencyBalance == 2
        && result.InventoryDelta.Slots.Count == 1
        && result.RewardProgressed is not null
        && document.RewardGrants.Count == 1
        && document.CurrencyLedger.Count == 1
        && document.Wallets.Single().Balance == 2
        && document.ItemInstances.Single().Location == "inventory"
        && document.OutboxEvents.Single().RewardKey == rewardKey
        && document.AuditEvents.Single().RewardKey == rewardKey;
}

static bool RewardKeyIdempotency()
{
    using var fixture = RewardRuntimeFixture.Create(new FixedRewardRandom([3], [100]));
    var first = fixture.Rewards.GrantMossSlimeReward("kill:idempotent", "char-idempotent");
    var second = fixture.Rewards.GrantMossSlimeReward("kill:idempotent", "char-idempotent");
    var document = fixture.Rewards.ReadDocumentForTesting();

    return first.Status == WorldRewardGrantStatus.Granted
        && second.Status == WorldRewardGrantStatus.AlreadyGranted
        && second.Idempotent
        && first.RewardGranted.RewardKey == second.RewardGranted.RewardKey
        && document.RewardGrants.Count == 1
        && document.CurrencyLedger.Count == 1
        && document.ItemInstances.Count == 1;
}

static bool TransientRetry()
{
    using var fixture = RewardRuntimeFixture.Create(new FixedRewardRandom([1], [5000]));
    fixture.Rewards.InjectFailureForTesting(WorldRewardFailureKind.Transient);

    var result = fixture.Rewards.GrantMossSlimeReward("kill:transient", "char-transient");
    var document = fixture.Rewards.ReadDocumentForTesting();

    return result.Status == WorldRewardGrantStatus.Granted
        && document.RewardGrants.Count == 1
        && document.CurrencyLedger.Count == 1
        && document.ItemInstances.Count == 0;
}

static bool DeadlockRetry()
{
    using var fixture = RewardRuntimeFixture.Create(new FixedRewardRandom([2], [49]));
    fixture.Rewards.InjectFailureForTesting(WorldRewardFailureKind.Deadlock, count: 2);

    var result = fixture.Rewards.GrantMossSlimeReward("kill:deadlock", "char-deadlock");
    var document = fixture.Rewards.ReadDocumentForTesting();

    return result.Status == WorldRewardGrantStatus.Granted
        && document.RewardGrants.Count == 1
        && document.CurrencyLedger.Count == 1
        && document.ItemInstances.Single().Rarity == MossSlimeRewardCatalog.RareRarity;
}

static bool FullInventoryPendingReward()
{
    using var fixture = RewardRuntimeFixture.Create(new FixedRewardRandom([2], [100]));
    fixture.Rewards.FillInventoryForTesting("char-full");

    var result = fixture.Rewards.GrantMossSlimeReward("kill:full", "char-full");
    var document = fixture.Rewards.ReadDocumentForTesting();
    var rewardItemId = result.RewardGranted.ItemInstanceIds.Single();

    return result.Status == WorldRewardGrantStatus.Granted
        && result.ItemPending
        && result.InventoryDelta.CurrencyBalance == 2
        && result.InventoryDelta.Slots.Count == 0
        && document.CurrencyLedger.Count == 1
        && document.PendingRewards.Single().ItemInstanceId == rewardItemId
        && document.ItemInstances.Single(item => item.ItemInstanceId == rewardItemId).Location == "pending_reward";
}

static bool LedgerAppendOnly()
{
    using var fixture = RewardRuntimeFixture.Create(new FixedRewardRandom([1, 2], [5000, 5000]));
    var first = fixture.Rewards.GrantMossSlimeReward("kill:first-ledger", "char-ledger");
    var firstLedger = fixture.Rewards.ReadDocumentForTesting().CurrencyLedger.Single();
    var second = fixture.Rewards.GrantMossSlimeReward("kill:second-ledger", "char-ledger");
    var document = fixture.Rewards.ReadDocumentForTesting();

    return first.Status == WorldRewardGrantStatus.Granted
        && second.Status == WorldRewardGrantStatus.Granted
        && document.CurrencyLedger.Count == 2
        && document.CurrencyLedger[0] == firstLedger
        && document.CurrencyLedger[1].Delta == 2
        && document.Wallets.Single().Balance == 3;
}

static bool OutboxAfterCommit()
{
    using var fixture = RewardRuntimeFixture.Create(new FixedRewardRandom([1], [5000]));
    var result = fixture.Rewards.GrantMossSlimeReward("kill:outbox", "char-outbox");
    var document = fixture.Rewards.ReadDocumentForTesting();

    return result.Status == WorldRewardGrantStatus.Granted
        && document.RewardGrants.Any(grant => grant.RewardKey == result.RewardKey)
        && document.OutboxEvents.SingleOrDefault(outbox => outbox.RewardKey == result.RewardKey) is not null
        && document.AuditEvents.SingleOrDefault(audit => audit.RewardKey == result.RewardKey) is not null;
}

static bool DeterministicQaLootTable()
{
    var rare = MossSlimeRewardCatalog.Roll(new FixedRewardRandom([1], [49]));
    var good = MossSlimeRewardCatalog.Roll(new FixedRewardRandom([1], [249]));
    var normal = MossSlimeRewardCatalog.Roll(new FixedRewardRandom([1], [1049]));
    var potion = MossSlimeRewardCatalog.Roll(new FixedRewardRandom([1], [1050]));
    var empty = MossSlimeRewardCatalog.Roll(new FixedRewardRandom([1], [3050]));

    return rare.ItemDrop is { ItemTemplateId: MossSlimeRewardCatalog.WoodenShieldTemplateId, Rarity: MossSlimeRewardCatalog.RareRarity }
        && good.ItemDrop is { ItemTemplateId: MossSlimeRewardCatalog.WoodenShieldTemplateId, Rarity: MossSlimeRewardCatalog.GoodRarity }
        && normal.ItemDrop is { ItemTemplateId: MossSlimeRewardCatalog.WoodenShieldTemplateId, Rarity: MossSlimeRewardCatalog.NormalRarity }
        && potion.ItemDrop is { ItemTemplateId: MossSlimeRewardCatalog.SmallPotionTemplateId, Rarity: MossSlimeRewardCatalog.NormalRarity }
        && empty.ItemDrop is null
        && MossSlimeRewardCatalog.CatalogVersion == "vs016.1"
        && rare.CurrencyAmount == 1;
}

static RewardTransactionCheck Check(string name, bool passed) => new(name, passed);

static async Task<RewardTransactionCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new RewardTransactionCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new RewardTransactionCheck(name, false);
    }
}

internal readonly record struct RewardTransactionCheck(string Name, bool Passed);

internal sealed class RewardRuntimeFixture : IDisposable
{
    private RewardRuntimeFixture(string storePath, WorldRewardRuntime rewards)
    {
        StorePath = storePath;
        Rewards = rewards;
    }

    public string StorePath { get; }
    public WorldRewardRuntime Rewards { get; }

    public static RewardRuntimeFixture Create(IRewardRandomSource random)
    {
        var storePath = TestPaths.CreateTempDirectory("vs016-rewards");
        var rewards = new WorldRewardRuntime(storePath, new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), random);
        return new RewardRuntimeFixture(storePath, rewards);
    }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}

internal sealed class RewardCombatFixture : IDisposable
{
    private RewardCombatFixture(
        string storePath,
        CharacterRecord character,
        WorldMonsterRuntime monsters,
        WorldCombatRuntime combat,
        WorldRewardRuntime rewards)
    {
        StorePath = storePath;
        Character = character;
        Monsters = monsters;
        Combat = combat;
        Rewards = rewards;
    }

    public string StorePath { get; }
    public CharacterRecord Character { get; }
    public WorldMonsterRuntime Monsters { get; }
    public WorldCombatRuntime Combat { get; }
    public WorldRewardRuntime Rewards { get; }
    public string TargetEntityId => Monsters.Monsters.First().EntityId;

    public static async Task<RewardCombatFixture> CreateAsync(IRewardRandomSource random)
    {
        var storePath = TestPaths.CreateTempDirectory("vs016-reward-combat");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var map = await CreateMapCatalogAsync(spawnX: 47, spawnY: 48);
        var store = new FileCharacterStore(storePath);
        var characters = new CharacterService(store, time);
        var create = await characters.CreateKnightAsync(new CreateKnightCommand("account-vs016", "Sir Reward"), CancellationToken.None);
        if (!create.Success || create.Character is null)
        {
            throw new InvalidOperationException(create.Message);
        }

        var movement = new WorldMovementRuntime(store, map, time);
        var monsters = new WorldMonsterRuntime(map, time);
        var rewards = new WorldRewardRuntime(storePath, time, random);
        var combat = new WorldCombatRuntime(movement, monsters, time, new FixedCombatRandom(1.0d, critical: false), rewards);
        _ = await movement.JoinAsync(create.Character, CancellationToken.None);
        return new RewardCombatFixture(storePath, create.Character, monsters, combat, rewards);
    }

    public WorldBasicAttackResult Attack(ulong sequence, string targetEntityId) =>
        Combat.ApplyBasicAttack(
            Character.CharacterId,
            sequence,
            new AttackIntent
            {
                TargetEntityId = targetEntityId,
                SkillId = BasicSlashCatalog.SkillId,
                ActionId = $"reward-action-{sequence}"
            });

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);

    private static async Task<WorldMapCatalog> CreateMapCatalogAsync(int spawnX, int spawnY)
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
            SafeSpawns = [new SafeSpawn { Id = "vs016-safe-spawn", X = spawnX, Y = spawnY }],
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
}

internal sealed class FixedRewardRandom : IRewardRandomSource
{
    private readonly Queue<int> _currency;
    private readonly Queue<int> _basisPoints;

    public FixedRewardRandom(IEnumerable<int> currency, IEnumerable<int> basisPoints)
    {
        _currency = new Queue<int>(currency);
        _basisPoints = new Queue<int>(basisPoints);
    }

    public int NextInclusive(int minValue, int maxValue)
    {
        var value = _currency.Count == 0 ? minValue : _currency.Dequeue();
        return Math.Max(minValue, Math.Min(maxValue, value));
    }

    public int NextBasisPoint() => _basisPoints.Count == 0 ? 5000 : _basisPoints.Dequeue();
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
}

internal static class TestPaths
{
    public static string CreateTempDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "divinity", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
