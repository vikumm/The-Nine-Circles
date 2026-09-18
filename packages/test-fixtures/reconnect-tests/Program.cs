using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Equipment;
using Divinity.GameRules.Inventory;
using Divinity.GameRules.Rewards;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Movement;
using Divinity.WorldRuntime.Rewards;

var checks = new List<ReconnectCheck>
{
    Check("reconnect within 30 seconds reassociates actor lease", ReconnectWithinThirtySeconds()),
    await CheckAsync("reconnect during combat keeps actor and HP", ReconnectDuringCombatKeepsActorAsync),
    await CheckAsync("reconnect after expiration saves checkpoint and exits map", ReconnectAfterExpirationAsync),
    Check("two competing reconnects have one owner", CompetingReconnectsHaveOneOwner()),
    Check("RewardGranted before drop is not duplicated", RewardGrantedBeforeDropDoesNotDuplicate()),
    await CheckAsync("unsafe checkpoint converts to safe spawn", UnsafeCheckpointConvertsToSafeSpawnAsync),
    Check("inventory and equipment persist after drop", InventoryEquipmentPersistAfterDrop()),
    Check("gateway session restart preserves reconnect token", GatewayRestartPreservesReconnectToken())
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-018 reconnect tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-018 reconnect tests passed.");
return 0;

static bool ReconnectWithinThirtySeconds()
{
    using var fixture = GatewaySessionFixture.Create("vs018-within-30");
    var join = fixture.Join("conn-a", "account-vs018-a", "character-vs018-a");
    fixture.Manager.Disconnect("conn-a", "disconnect_abrupt", preserveLeaseForReconnect: true);

    fixture.Time.Advance(TimeSpan.FromSeconds(29));
    fixture.Authenticate("conn-b", "account-vs018-a");
    var reconnect = fixture.Manager.TryReconnect("conn-b", join.Token, "conn-a");

    return reconnect.Status == ReconnectLeaseStatus.Reconnected
        && reconnect.Lease?.ConnectionId == "conn-b"
        && reconnect.Lease.CharacterId == "character-vs018-a"
        && reconnect.ReconnectToken is not null
        && fixture.Manager.GetSession("conn-a") is null
        && fixture.Manager.GetSession("conn-b")?.CharacterId == "character-vs018-a";
}

static async Task<bool> ReconnectDuringCombatKeepsActorAsync()
{
    using var world = await ReconnectWorldFixture.CreateAsync("vs018-combat-grace");
    var character = await world.CreateKnightAsync("account-vs018-combat", "Sir Reconnect Combat");
    _ = await world.Movement.JoinAsync(character, CancellationToken.None);
    var moved = await world.Movement.ApplyMoveAsync(
        character.CharacterId,
        sequence: 1,
        clientTick: 1,
        new MoveIntent { Mode = MovementMode.Direction, DirectionX = 1, DirectionY = 0 },
        CancellationToken.None);
    var damaged = world.Movement.ApplyDamageToCharacter(character.CharacterId, 7, "mob:moss-slime", "moss_slime_attack");
    var disconnect = await world.Movement.DisconnectAsync(character.CharacterId, "disconnect_abrupt", CancellationToken.None);

    world.Time.Advance(TimeSpan.FromSeconds(5));
    var rejoined = await world.Movement.JoinAsync(character, CancellationToken.None);

    return damaged.Accepted
        && moved.Accepted
        && disconnect.ActorRetained
        && world.Movement.ActiveActorCount == 1
        && rejoined.Position == moved.Position
        && rejoined.Stats.Hp == character.Stats.MaxHp - 7
        && rejoined.Snapshot.Entities.Single().Hp == character.Stats.MaxHp - 7;
}

static async Task<bool> ReconnectAfterExpirationAsync()
{
    using var world = await ReconnectWorldFixture.CreateAsync("vs018-expired-world");
    var character = await world.CreateKnightAsync("account-vs018-expired", "Sir Expired");
    _ = await world.Movement.JoinAsync(character, CancellationToken.None);
    world.Movement.MarkCombatActivity(character.CharacterId);
    var disconnect = await world.Movement.DisconnectAsync(character.CharacterId, "disconnect_abrupt", CancellationToken.None);

    world.Time.Advance(WorldMovementDefaults.CombatDisconnectGrace + TimeSpan.FromMilliseconds(1));
    var expiredActors = await world.Movement.ExpireDisconnectGraceAsync(CancellationToken.None);
    var checkpoint = await world.Store.GetCheckpointAsync(character.CharacterId, CancellationToken.None);

    using var session = GatewaySessionFixture.Create("vs018-expired-session");
    var join = session.Join("conn-expired-a", "account-vs018-expired", character.CharacterId);
    session.Manager.Disconnect("conn-expired-a", "disconnect_abrupt", preserveLeaseForReconnect: true);
    session.Time.Advance(GatewaySessionDefaults.ReconnectGrace + TimeSpan.FromMilliseconds(1));
    session.Authenticate("conn-expired-b", "account-vs018-expired");
    var reconnect = session.Manager.TryReconnect("conn-expired-b", join.Token, "conn-expired-a");

    return disconnect.ActorRetained
        && expiredActors.Single().CharacterId == character.CharacterId
        && world.Movement.ActiveActorCount == 0
        && checkpoint?.Reason == "disconnect_grace_expired"
        && reconnect.Status == ReconnectLeaseStatus.InvalidToken;
}

static bool CompetingReconnectsHaveOneOwner()
{
    using var fixture = GatewaySessionFixture.Create("vs018-competing");
    var join = fixture.Join("conn-compete-a", "account-vs018-compete", "character-vs018-compete");
    fixture.Manager.Disconnect("conn-compete-a", "disconnect_abrupt", preserveLeaseForReconnect: true);
    fixture.Authenticate("conn-compete-b", "account-vs018-compete");
    fixture.Authenticate("conn-compete-c", "account-vs018-compete");

    var first = fixture.Manager.TryReconnect("conn-compete-b", join.Token, "conn-compete-a");
    var replay = fixture.Manager.TryReconnect("conn-compete-c", join.Token, "conn-compete-a");
    var freshJoin = fixture.Manager.TryJoin("conn-compete-c", "character-vs018-compete");

    return first.Status == ReconnectLeaseStatus.Reconnected
        && replay.Status == ReconnectLeaseStatus.InvalidToken
        && freshJoin.Status == JoinLeaseStatus.LeaseConflict
        && fixture.Manager.GetLease("character-vs018-compete")?.ConnectionId == "conn-compete-b";
}

static bool RewardGrantedBeforeDropDoesNotDuplicate()
{
    using var fixture = RewardFixture.Create();
    var first = fixture.Rewards.GrantMossSlimeReward("kill:vs018-reconnect", "character-vs018-reward");
    var second = fixture.Rewards.GrantMossSlimeReward("kill:vs018-reconnect", "character-vs018-reward");
    var document = fixture.Rewards.ReadDocumentForTesting();

    return first.Status == WorldRewardGrantStatus.Granted
        && second.Status == WorldRewardGrantStatus.AlreadyGranted
        && second.Idempotent
        && document.RewardGrants.Count == 1
        && document.OutboxEvents.Count == 1;
}

static async Task<bool> UnsafeCheckpointConvertsToSafeSpawnAsync()
{
    using var world = await ReconnectWorldFixture.CreateAsync("vs018-unsafe-checkpoint");
    var character = await world.CreateKnightAsync("account-vs018-unsafe", "Sir Safe Spawn");
    var unsafeCheckpoint = new CharacterCheckpointRecord(
        character.CharacterId,
        character.MapId,
        new CharacterPosition(-5, -5),
        LastSequence: 42,
        "test_unsafe",
        world.Time.GetUtcNow(),
        CurrentHp: 11,
        CurrentMp: 3,
        Level: character.Stats.Level);
    await world.Store.StoreCheckpointAsync(unsafeCheckpoint, CancellationToken.None);

    var joined = await world.Movement.JoinAsync(character, CancellationToken.None);

    return joined.Position == character.SafeSpawn
        && joined.Stats.Hp == 11
        && joined.Stats.Mp == 3
        && joined.Snapshot.Entities.Single().Position.X == (float)character.SafeSpawn.X;
}

static bool InventoryEquipmentPersistAfterDrop()
{
    using var fixture = InventoryReconnectFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, EquipmentRarity.Rare);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var equip = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));

    var join = fixture.Sessions.Join("conn-inv-a", fixture.Actor.CharacterId, fixture.Actor.CharacterId);
    fixture.Sessions.Manager.Disconnect("conn-inv-a", "disconnect_abrupt", preserveLeaseForReconnect: true);
    fixture.Sessions.Authenticate("conn-inv-b", fixture.Actor.CharacterId);
    var reconnect = fixture.Sessions.Manager.TryReconnect("conn-inv-b", join.Token, "conn-inv-a");
    var after = fixture.Inventory.GetOrCreateInventory(fixture.Actor);

    return equip.Accepted
        && reconnect.Status == ReconnectLeaseStatus.Reconnected
        && after.Equipment.Single().ItemInstanceId == item.ItemInstanceId
        && after.Equipment.Single().DefenseBonus == 5
        && after.Slots.All(slot => slot.ItemInstanceId != item.ItemInstanceId);
}

static bool GatewayRestartPreservesReconnectToken()
{
    using var fixture = GatewaySessionFixture.Create("vs018-restart");
    var join = fixture.Join("conn-restart-a", "account-vs018-restart", "character-vs018-restart");
    fixture.Manager.Disconnect("conn-restart-a", "disconnect_abrupt", preserveLeaseForReconnect: true);

    var restarted = new GatewaySessionManager(fixture.Time, fixture.StorePath);
    _ = restarted.CreateAuthenticatedSession("conn-restart-b", TestTickets.Create("account-vs018-restart", "nonce-conn-restart-b", fixture.Time.GetUtcNow()));
    var reconnect = restarted.TryReconnect("conn-restart-b", join.Token, "conn-restart-a");

    return reconnect.Status == ReconnectLeaseStatus.Reconnected
        && reconnect.Lease?.ConnectionId == "conn-restart-b"
        && restarted.ReadDocumentForTesting().SessionLeases.Single().CharacterId == "character-vs018-restart";
}

static ReconnectCheck Check(string name, bool passed) => new(name, passed);

static async Task<ReconnectCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new ReconnectCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new ReconnectCheck(name, false);
    }
}

internal readonly record struct ReconnectCheck(string Name, bool Passed);

internal static class TestTickets
{
    public static StoredGameTicket Create(string accountId, string nonce, DateTimeOffset now) =>
        new(
            accountId,
            GameTicketDefaults.DefaultBuildId,
            ProtocolConstants.SupportedProtocolVersion,
            nonce,
            now,
            now.Add(GameTicketDefaults.TimeToLive));
}

internal sealed class GatewaySessionFixture : IDisposable
{
    private GatewaySessionFixture(string storePath, ManualTimeProvider time, GatewaySessionManager manager)
    {
        StorePath = storePath;
        Time = time;
        Manager = manager;
    }

    public string StorePath { get; }
    public ManualTimeProvider Time { get; }
    public GatewaySessionManager Manager { get; }

    public static GatewaySessionFixture Create(string name)
    {
        var storePath = TestPaths.CreateTempDirectory(name);
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var manager = new GatewaySessionManager(time, storePath);
        return new GatewaySessionFixture(storePath, time, manager);
    }

    public void Authenticate(string connectionId, string accountId) =>
        Manager.CreateAuthenticatedSession(connectionId, TestTickets.Create(accountId, "nonce-" + connectionId, Time.GetUtcNow()));

    public JoinedSession Join(string connectionId, string accountId, string characterId)
    {
        Authenticate(connectionId, accountId);
        var result = Manager.TryJoin(connectionId, characterId);
        if (result.Status != JoinLeaseStatus.Joined || result.ReconnectToken is null)
        {
            throw new InvalidOperationException(result.Message);
        }

        return new JoinedSession(result.ReconnectToken.Token, result.ReconnectToken.ExpiresAtUtc);
    }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}

internal sealed class ReconnectWorldFixture : IDisposable
{
    private ReconnectWorldFixture(
        string storePath,
        ManualTimeProvider time,
        FileCharacterStore store,
        CharacterService characters,
        WorldMovementRuntime movement)
    {
        StorePath = storePath;
        Time = time;
        Store = store;
        Characters = characters;
        Movement = movement;
    }

    public string StorePath { get; }
    public ManualTimeProvider Time { get; }
    public FileCharacterStore Store { get; }
    public CharacterService Characters { get; }
    public WorldMovementRuntime Movement { get; }

    public static async Task<ReconnectWorldFixture> CreateAsync(string name)
    {
        var storePath = TestPaths.CreateTempDirectory(name);
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new FileCharacterStore(storePath);
        var characters = new CharacterService(store, time);
        var map = await WorldMapCatalog.LoadDefaultAsync();
        var movement = new WorldMovementRuntime(store, map, time);
        return new ReconnectWorldFixture(storePath, time, store, characters, movement);
    }

    public async Task<CharacterRecord> CreateKnightAsync(string accountId, string name)
    {
        var result = await Characters.CreateKnightAsync(new CreateKnightCommand(accountId, name), CancellationToken.None);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.Character!;
    }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}

internal sealed class RewardFixture : IDisposable
{
    private RewardFixture(string storePath, WorldRewardRuntime rewards)
    {
        StorePath = storePath;
        Rewards = rewards;
    }

    public string StorePath { get; }
    public WorldRewardRuntime Rewards { get; }

    public static RewardFixture Create()
    {
        var storePath = TestPaths.CreateTempDirectory("vs018-rewards");
        var rewards = new WorldRewardRuntime(storePath, new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), new FixedRewardRandom());
        return new RewardFixture(storePath, rewards);
    }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}

internal sealed class InventoryReconnectFixture : IDisposable
{
    private InventoryReconnectFixture(string storePath, GatewaySessionFixture sessions, InventoryEquipmentService inventory, InventoryActor actor)
    {
        StorePath = storePath;
        Sessions = sessions;
        Inventory = inventory;
        Actor = actor;
    }

    public string StorePath { get; }
    public GatewaySessionFixture Sessions { get; }
    public InventoryEquipmentService Inventory { get; }
    public InventoryActor Actor { get; }

    public static InventoryReconnectFixture Create()
    {
        var storePath = TestPaths.CreateTempDirectory("vs018-inventory");
        var sessions = GatewaySessionFixture.Create("vs018-inventory-session");
        var inventory = new InventoryEquipmentService(storePath, sessions.Time);
        var actor = new InventoryActor("character-vs018-inventory", "Knight", 1);
        return new InventoryReconnectFixture(storePath, sessions, inventory, actor);
    }

    public void Dispose()
    {
        Sessions.Dispose();
        TestPaths.DeleteDirectory(StorePath);
    }
}

internal readonly record struct JoinedSession(string Token, DateTimeOffset ExpiresAtUtc);

internal sealed class FixedRewardRandom : IRewardRandomSource
{
    public int NextInclusive(int minInclusive, int maxInclusive) => minInclusive;

    public int NextBasisPoint() => 5_000;
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
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
