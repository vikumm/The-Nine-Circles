using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Movement;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Movement;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var checks = new List<MovementCheck>();

checks.Add(Check("diagonal movement normalizes to unit length", DiagonalNormalizes()));
checks.Add(await CheckAsync("WASD movement respects 4.5 units/s per tick", WasdSpeedLimitAsync));
checks.Add(await CheckAsync("click-to-move accepts navigable target", ClickNavigableTargetAcceptedAsync));
checks.Add(await CheckAsync("click-to-move crossing wall is rejected", WallCrossingRejectedAsync));
checks.Add(await CheckAsync("click outside map is rejected", ClickOutsideMapRejectedAsync));
checks.Add(await CheckAsync("click on blocked cell is rejected", ClickBlockedCellRejectedAsync));
checks.Add(await CheckAsync("repeated sequence is rejected", RepeatedSequenceRejectedAsync));
checks.Add(await CheckAsync("dead and stunned characters cannot move", DeadAndStunnedRejectedAsync));
checks.Add(await CheckAsync("snapshot publishes at 10 Hz", SnapshotPublishesAtTenHzAsync));
checks.Add(await CheckAsync("checkpoint is saved periodically and on disconnect", CheckpointPeriodicAndDisconnectAsync));
checks.Add(await CheckAsync("checkpoint is saved on graceful shutdown", CheckpointShutdownAsync));
checks.Add(Check("gateway move intent rate limit is 20 per second", GatewayMoveIntentRateLimit()));
checks.Add(await CheckAsync("gateway accepts MoveIntent and returns authoritative snapshot", GatewayMoveIntentSnapshotAsync));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-010 movement tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-010 movement tests passed.");
return 0;

static bool DiagonalNormalizes()
{
    var normalized = MovementMath.NormalizeDirection(1, 1);
    var length = Math.Sqrt(normalized.X * normalized.X + normalized.Y * normalized.Y);
    var facing = MovementMath.ResolveFacing(normalized.X, normalized.Y, CardinalDirection.South);

    return Math.Abs(length - 1) < 0.000001d
        && normalized.X < 1
        && normalized.Y < 1
        && facing == CardinalDirection.South;
}

static async Task<bool> WasdSpeedLimitAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-speed");
    var character = await fixture.CreateKnightAsync("account-vs010-speed", "Sir Speed");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    var result = await fixture.Runtime.ApplyMoveAsync(
        character.CharacterId,
        sequence: 1,
        clientTick: 1,
        CreateDirectionMove(99, 0),
        CancellationToken.None);

    var distance = (double)(result.Position.X - character.SafeSpawn.X);
    return result.Status == WorldMovementStatus.Accepted
        && distance <= WorldMovementDefaults.MaxDistancePerMoveIntent + 0.000001d
        && Math.Abs(distance - WorldMovementDefaults.MaxDistancePerMoveIntent) < 0.000001d;
}

static async Task<bool> ClickNavigableTargetAcceptedAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-click-ok");
    var character = await fixture.CreateKnightAsync("account-vs010-click-ok", "Sir Click");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    var result = await fixture.Runtime.ApplyMoveAsync(
        character.CharacterId,
        sequence: 1,
        clientTick: 1,
        CreateClickMove(10, 8),
        CancellationToken.None);

    return result.Status == WorldMovementStatus.Accepted
        && result.Position.X > character.SafeSpawn.X
        && result.Position.Y == character.SafeSpawn.Y;
}

static async Task<bool> WallCrossingRejectedAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-wall");
    var character = await fixture.CreateKnightAsync("account-vs010-wall", "Sir Wall");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    var result = await fixture.Runtime.ApplyMoveAsync(
        character.CharacterId,
        sequence: 1,
        clientTick: 1,
        CreateClickMove(50, 50),
        CancellationToken.None);

    return result.Status == WorldMovementStatus.Collision
        && result.Correction?.Reason == CorrectionReason.Collision
        && result.Position == character.SafeSpawn;
}

static async Task<bool> ClickOutsideMapRejectedAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-outside");
    var character = await fixture.CreateKnightAsync("account-vs010-outside", "Sir Outside");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    var result = await fixture.Runtime.ApplyMoveAsync(
        character.CharacterId,
        sequence: 1,
        clientTick: 1,
        CreateClickMove(999, 999),
        CancellationToken.None);

    return result.Status == WorldMovementStatus.TargetOutOfBounds
        && result.Correction?.Reason == CorrectionReason.ProtocolRejected;
}

static async Task<bool> ClickBlockedCellRejectedAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-blocked");
    var character = await fixture.CreateKnightAsync("account-vs010-blocked", "Sir Blocked");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    var result = await fixture.Runtime.ApplyMoveAsync(
        character.CharacterId,
        sequence: 1,
        clientTick: 1,
        CreateClickMove(32.5f, 40.5f),
        CancellationToken.None);

    return result.Status == WorldMovementStatus.BlockedDestination
        && result.Correction?.Reason == CorrectionReason.Collision;
}

static async Task<bool> RepeatedSequenceRejectedAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-sequence");
    var character = await fixture.CreateKnightAsync("account-vs010-sequence", "Sir Sequence");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    var first = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 1, 1, CreateDirectionMove(1, 0), CancellationToken.None);
    var repeated = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 1, 2, CreateDirectionMove(1, 0), CancellationToken.None);

    return first.Status == WorldMovementStatus.Accepted
        && repeated.Status == WorldMovementStatus.SequenceReplay
        && repeated.Correction?.Reason == CorrectionReason.SequenceReplay
        && repeated.Position == first.Position;
}

static async Task<bool> DeadAndStunnedRejectedAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-state");
    var character = await fixture.CreateKnightAsync("account-vs010-state", "Sir State");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    fixture.Runtime.SetCharacterMotionState(character.CharacterId, WorldCharacterMotionState.Dead);
    var dead = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 1, 1, CreateDirectionMove(1, 0), CancellationToken.None);

    fixture.Runtime.SetCharacterMotionState(character.CharacterId, WorldCharacterMotionState.Stunned);
    var stunned = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 2, 2, CreateDirectionMove(1, 0), CancellationToken.None);

    return dead.Status == WorldMovementStatus.MovementBlockedByState
        && stunned.Status == WorldMovementStatus.MovementBlockedByState
        && dead.Position == character.SafeSpawn
        && stunned.Position == character.SafeSpawn;
}

static async Task<bool> SnapshotPublishesAtTenHzAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-snapshot");
    var character = await fixture.CreateKnightAsync("account-vs010-snapshot", "Sir Snapshot");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    fixture.Time.Advance(TimeSpan.FromMilliseconds(99));
    var early = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 1, 1, CreateDirectionMove(1, 0), CancellationToken.None);
    fixture.Time.Advance(TimeSpan.FromMilliseconds(1));
    var due = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 2, 2, CreateDirectionMove(1, 0), CancellationToken.None);

    return early.Snapshot is null
        && due.Snapshot is not null
        && due.Snapshot.MapId == KnightCatalog.MapId
        && due.Snapshot.Entities.Count == 1
        && due.Snapshot.Entities[0].EntityId == character.CharacterId;
}

static async Task<bool> CheckpointPeriodicAndDisconnectAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-checkpoint");
    var character = await fixture.CreateKnightAsync("account-vs010-checkpoint", "Sir Checkpoint");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    fixture.Time.Advance(WorldMovementDefaults.CheckpointInterval);
    var periodic = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 1, 1, CreateDirectionMove(1, 0), CancellationToken.None);
    var periodicCheckpoint = await fixture.Store.GetCheckpointAsync(character.CharacterId, CancellationToken.None);

    fixture.Time.Advance(TimeSpan.FromSeconds(1));
    _ = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 2, 2, CreateDirectionMove(0, 1), CancellationToken.None);
    await fixture.Runtime.DisconnectAsync(character.CharacterId, "disconnect_normal", CancellationToken.None);
    var disconnectCheckpoint = await fixture.Store.GetCheckpointAsync(character.CharacterId, CancellationToken.None);

    return periodic.CheckpointStored
        && periodicCheckpoint?.Reason == "periodic"
        && disconnectCheckpoint?.Reason == "disconnect_normal"
        && disconnectCheckpoint.Position != character.SafeSpawn;
}

static async Task<bool> CheckpointShutdownAsync()
{
    using var fixture = await MovementFixture.CreateAsync("vs010-shutdown");
    var character = await fixture.CreateKnightAsync("account-vs010-shutdown", "Sir Shutdown");
    _ = await fixture.Runtime.JoinAsync(character, CancellationToken.None);

    _ = await fixture.Runtime.ApplyMoveAsync(character.CharacterId, 1, 1, CreateDirectionMove(1, 0), CancellationToken.None);
    await fixture.Runtime.SaveAllCheckpointsAsync("shutdown", CancellationToken.None);
    var shutdownCheckpoint = await fixture.Store.GetCheckpointAsync(character.CharacterId, CancellationToken.None);

    return shutdownCheckpoint?.Reason == "shutdown"
        && shutdownCheckpoint.Position != character.SafeSpawn;
}

static bool GatewayMoveIntentRateLimit()
{
    var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    var time = new ManualTimeProvider(now);
    var manager = new GatewaySessionManager(time);
    var ticket = new StoredGameTicket(
        "account-vs010-rate",
        GameTicketDefaults.DefaultBuildId,
        ProtocolConstants.SupportedProtocolVersion,
        "nonce-vs010-rate",
        now,
        now.Add(GameTicketDefaults.TimeToLive));

    _ = manager.CreateAuthenticatedSession("connection-vs010-rate", ticket);

    var accepted = Enumerable
        .Range(0, GatewaySessionDefaults.MoveIntentLimit)
        .Select(_ => manager.TryAcquireMoveIntent("connection-vs010-rate"))
        .All(result => result.Status == MoveIntentRateLimitStatus.Accepted);

    var rejected = manager.TryAcquireMoveIntent("connection-vs010-rate");
    time.Advance(GatewaySessionDefaults.MoveIntentWindow);
    var acceptedAfterWindow = manager.TryAcquireMoveIntent("connection-vs010-rate");

    return accepted
        && rejected.Status == MoveIntentRateLimitStatus.RateLimited
        && acceptedAfterWindow.Status == MoveIntentRateLimitStatus.Accepted;
}

static async Task<bool> GatewayMoveIntentSnapshotAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs010-gateway-move");
    var accountId = "account-vs010-gateway";
    var nonce = "nonce-vs010-gateway";
    var character = await fixture.CreateKnightAsync(accountId, "Sir Gateway");
    var ticket = await fixture.IssueTicketAsync(accountId, nonce);

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await SendEnvelopeAsync(socket, CreateJoinWorld(character.CharacterId, sequence: 2));
    _ = await ReceiveServerEnvelopeAsync(socket);
    _ = await ReceiveServerEnvelopeAsync(socket);

    await Task.Delay(125);
    await SendEnvelopeAsync(socket, CreateDirectionEnvelope(sequence: 3, clientTick: 3, 1, 0));
    var move = await ReceiveServerEnvelopeAsync(socket);
    await CloseNormalAsync(socket);

    return move.PayloadCase == ServerEnvelope.PayloadOneofCase.WorldSnapshot
        && move.WorldSnapshot.Entities.Count == 1
        && move.WorldSnapshot.Entities[0].EntityId == character.CharacterId
        && move.WorldSnapshot.Entities[0].Position.X > (float)character.SafeSpawn.X;
}

static MoveIntent CreateDirectionMove(float x, float y) =>
    new()
    {
        Mode = MovementMode.Direction,
        DirectionX = x,
        DirectionY = y
    };

static MoveIntent CreateClickMove(float x, float y) =>
    new()
    {
        Mode = MovementMode.ClickTarget,
        TargetX = x,
        TargetY = y
    };

static ClientEnvelope CreateHello(string ticket, string nonce, ulong sequence) =>
    new()
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = sequence,
        ClientTick = sequence,
        ClientHello = new ClientHello
        {
            BuildId = GameTicketDefaults.DefaultBuildId,
            GameTicket = ticket,
            ClientNonce = nonce
        }
    };

static ClientEnvelope CreateJoinWorld(string characterId, ulong sequence) =>
    new()
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = sequence,
        ClientTick = sequence,
        JoinWorld = new JoinWorld
        {
            CharacterId = characterId,
            RequestedMapId = KnightCatalog.MapId,
            ContentHash = KnightCatalog.ContentHash
        }
    };

static ClientEnvelope CreateDirectionEnvelope(ulong sequence, ulong clientTick, float x, float y) =>
    new()
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = sequence,
        ClientTick = clientTick,
        MoveIntent = CreateDirectionMove(x, y)
    };

static async Task SendEnvelopeAsync(ClientWebSocket socket, ClientEnvelope envelope)
{
    var bytes = envelope.ToByteArray();
    await socket.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
}

static async Task<ServerEnvelope> ReceiveServerEnvelopeAsync(ClientWebSocket socket)
{
    var buffer = new byte[8192];
    using var payload = new MemoryStream();

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            throw new InvalidOperationException("WebSocket closed before a ServerEnvelope was received.");
        }

        payload.Write(buffer, 0, result.Count);
        if (result.EndOfMessage)
        {
            return ServerEnvelope.Parser.ParseFrom(payload.ToArray());
        }
    }
}

static async Task CloseNormalAsync(ClientWebSocket socket)
{
    if (socket.State == WebSocketState.Open)
    {
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }
}

static MovementCheck Check(string name, bool passed) => new(name, passed);

static async Task<MovementCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new MovementCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new MovementCheck(name, false);
    }
}

internal readonly record struct MovementCheck(string Name, bool Passed);

internal sealed class MovementFixture : IDisposable
{
    private MovementFixture(string storePath, FileCharacterStore store, CharacterService characterService, WorldMovementRuntime runtime, ManualTimeProvider time)
    {
        StorePath = storePath;
        Store = store;
        CharacterService = characterService;
        Runtime = runtime;
        Time = time;
    }

    public string StorePath { get; }
    public FileCharacterStore Store { get; }
    public CharacterService CharacterService { get; }
    public WorldMovementRuntime Runtime { get; }
    public ManualTimeProvider Time { get; }

    public static async Task<MovementFixture> CreateAsync(string name)
    {
        var storePath = TestPaths.CreateTempDirectory(name);
        var store = new FileCharacterStore(storePath);
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var catalog = await WorldMapCatalog.LoadDefaultAsync();
        var runtime = new WorldMovementRuntime(store, catalog, time);
        var characterService = new CharacterService(store, time);
        return new MovementFixture(storePath, store, characterService, runtime, time);
    }

    public async Task<CharacterRecord> CreateKnightAsync(string accountId, string name)
    {
        var result = await CharacterService.CreateKnightAsync(new CreateKnightCommand(accountId, name), CancellationToken.None);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.Character!;
    }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
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

internal sealed class GatewayFixture : IAsyncDisposable
{
    private readonly string? _previousTicketStorePath;
    private readonly string? _previousCharacterStorePath;

    private GatewayFixture(WebApplication app, string httpBaseUrl, string ticketStorePath, string characterStorePath, string? previousTicketStorePath, string? previousCharacterStorePath)
    {
        App = app;
        HttpBaseUrl = httpBaseUrl;
        TicketStorePath = ticketStorePath;
        CharacterStorePath = characterStorePath;
        _previousTicketStorePath = previousTicketStorePath;
        _previousCharacterStorePath = previousCharacterStorePath;
        TicketService = new GameTicketService(new FileGameTicketStore(ticketStorePath));
        CharacterService = new CharacterService(new FileCharacterStore(characterStorePath));
    }

    public WebApplication App { get; }
    public string HttpBaseUrl { get; }
    public string TicketStorePath { get; }
    public string CharacterStorePath { get; }
    public GameTicketService TicketService { get; }
    public CharacterService CharacterService { get; }

    public static async Task<GatewayFixture> StartAsync(string name)
    {
        var ticketStorePath = TestPaths.CreateTempDirectory(name + "-tickets");
        var characterStorePath = TestPaths.CreateTempDirectory(name + "-characters");
        var previousTicketStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
        var previousCharacterStorePath = Environment.GetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH");
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", ticketStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH", characterStorePath);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        builder.Logging.ClearProviders();

        var app = GatewayApp.Build(builder);
        var httpBaseUrl = $"http://127.0.0.1:{TestPaths.GetFreeTcpPort()}";
        app.Urls.Add(httpBaseUrl);
        await app.StartAsync();

        return new GatewayFixture(app, httpBaseUrl, ticketStorePath, characterStorePath, previousTicketStorePath, previousCharacterStorePath);
    }

    public async Task<ClientWebSocket> ConnectAsync()
    {
        var socket = new ClientWebSocket();
        var wsUrl = HttpBaseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/protocol/v1/ws";
        await socket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        return socket;
    }

    public async Task<string> IssueTicketAsync(string accountId, string nonce)
    {
        var result = await TicketService.IssueAsync(
            new GameTicketIssueCommand(accountId, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, nonce),
            CancellationToken.None);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.GameTicket!;
    }

    public async Task<CharacterRecord> CreateKnightAsync(string accountId, string name)
    {
        var result = await CharacterService.CreateKnightAsync(new CreateKnightCommand(accountId, name), CancellationToken.None);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.Character!;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", _previousTicketStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH", _previousCharacterStorePath);
        TestPaths.DeleteDirectory(TicketStorePath);
        TestPaths.DeleteDirectory(CharacterStorePath);
    }
}

internal static class TestPaths
{
    public static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

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
