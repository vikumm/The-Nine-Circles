using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var checks = new List<SessionCheck>();

checks.Add(await CheckAsync("valid WSS handshake and authenticated join", ValidHandshakeAndJoinAsync));
checks.Add(await CheckAsync("missing ticket is rejected", MissingTicketRejectedAsync));
checks.Add(await CheckAsync("expired ticket is rejected", ExpiredTicketRejectedAsync));
checks.Add(await CheckAsync("reused ticket is rejected", ReusedTicketRejectedAsync));
checks.Add(await CheckAsync("incompatible ticket is rejected", IncompatibleTicketRejectedAsync));
checks.Add(await CheckAsync("large WSS payload is rejected before parse", LargePayloadRejectedAsync));
checks.Add(await CheckAsync("truncated Protobuf is rejected", TruncatedPayloadRejectedAsync));
checks.Add(await CheckAsync("anonymous handshake rate limit is enforced", AnonymousRateLimitAsync));
checks.Add(await CheckAsync("heartbeat renews session lease", HeartbeatRenewsLeaseAsync));
checks.Add(await CheckAsync("two concurrent joins for one character have one owner", ConcurrentJoinHasSingleOwnerAsync));
checks.Add(await CheckAsync("normal disconnect releases lease and records event", NormalDisconnectRecordsEventAsync));
checks.Add(await CheckAsync("session logs include connection metadata without secrets", LogsOmitSecretsAsync));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-007 session tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-007 session tests passed.");
return 0;

static async Task<bool> ValidHandshakeAndJoinAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-valid");
    var accountId = "account-vs007-valid";
    var nonce = "nonce-vs007-valid";
    var ticket = await fixture.IssueTicketAsync(accountId, nonce);
    var character = await fixture.CreateKnightAsync(accountId, "Sir Session Valid");

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    var hello = await ReceiveServerEnvelopeAsync(socket);

    await SendEnvelopeAsync(socket, CreateJoinWorld(character.CharacterId, sequence: 2));
    var join = await ReceiveServerEnvelopeAsync(socket);

    var lease = fixture.SessionManager.GetLease(character.CharacterId);
    await CloseNormalAsync(socket);

    return hello.ServerError.Code == ErrorCode.ClientHelloAcceptedSession
        && join.PayloadCase == ServerEnvelope.PayloadOneofCase.JoinAccepted
        && join.JoinAccepted.CharacterId == character.CharacterId
        && lease is not null;
}

static async Task<bool> MissingTicketRejectedAsync() =>
    await HandshakeErrorAsync(
        fixture => Task.FromResult(string.Empty),
        ticket => CreateHello(ticket, "nonce-vs007-missing", sequence: 1),
        ErrorCode.GameTicketRequired,
        "vs007-missing");

static async Task<bool> ExpiredTicketRejectedAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-expired");
    var accountId = "account-vs007-expired";
    var nonce = "nonce-vs007-expired";
    var ticket = GameTicketSecret.Create();
    await fixture.Store.StoreAsync(
        GameTicketSecret.Hash(ticket),
        new StoredGameTicket(
            accountId,
            GameTicketDefaults.DefaultBuildId,
            ProtocolConstants.SupportedProtocolVersion,
            nonce,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddSeconds(-1)),
        GameTicketDefaults.TimeToLive,
        CancellationToken.None);

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    var response = await ReceiveServerEnvelopeAsync(socket);
    await WaitForCloseAsync(socket);

    return response.ServerError.Code == ErrorCode.GameTicketExpired;
}

static async Task<bool> ReusedTicketRejectedAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-reused");
    var accountId = "account-vs007-reused";
    var nonce = "nonce-vs007-reused";
    var ticket = await fixture.IssueTicketAsync(accountId, nonce);
    _ = await fixture.TicketService.ConsumeAsync(
        new GameTicketConsumeCommand(ticket, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, nonce),
        CancellationToken.None);

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    var response = await ReceiveServerEnvelopeAsync(socket);
    await WaitForCloseAsync(socket);

    return response.ServerError.Code == ErrorCode.GameTicketReused;
}

static async Task<bool> IncompatibleTicketRejectedAsync() =>
    await HandshakeErrorAsync(
        fixture => fixture.IssueTicketAsync("account-vs007-incompatible", "nonce-vs007-incompatible"),
        ticket => CreateHello(ticket, "nonce-vs007-incompatible", buildId: "wrong-build", sequence: 1),
        ErrorCode.GameTicketBuildMismatch,
        "vs007-incompatible");

static async Task<bool> LargePayloadRejectedAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-large");
    using var socket = await fixture.ConnectAsync();
    await socket.SendAsync(new byte[ProtocolConstants.MaxEnvelopeBytes + 1], WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
    var response = await ReceiveServerEnvelopeAsync(socket);
    await WaitForCloseAsync(socket);

    return response.ServerError.Code == ErrorCode.PayloadTooLarge;
}

static async Task<bool> TruncatedPayloadRejectedAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-truncated");
    using var socket = await fixture.ConnectAsync();
    await socket.SendAsync(new byte[] { 0x0a, 0xff }, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
    var response = await ReceiveServerEnvelopeAsync(socket);
    await WaitForCloseAsync(socket);

    return response.ServerError.Code == ErrorCode.MalformedPayload;
}

static async Task<bool> AnonymousRateLimitAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-rate-limit");
    using var client = new HttpClient { BaseAddress = new Uri(fixture.HttpBaseUrl) };
    var statuses = new List<HttpStatusCode>();

    for (var attempt = 0; attempt < GatewaySessionDefaults.AnonymousHandshakeLimit + 1; attempt++)
    {
        using var response = await client.GetAsync("/protocol/v1/ws");
        statuses.Add(response.StatusCode);
    }

    return statuses.Take(GatewaySessionDefaults.AnonymousHandshakeLimit).All(status => status == HttpStatusCode.BadRequest)
        && statuses.Last() == HttpStatusCode.TooManyRequests;
}

static async Task<bool> HeartbeatRenewsLeaseAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-heartbeat");
    var accountId = "account-vs007-heartbeat";
    var nonce = "nonce-vs007-heartbeat";
    var ticket = await fixture.IssueTicketAsync(accountId, nonce);
    var character = await fixture.CreateKnightAsync(accountId, "Sir Session Beat");

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await SendEnvelopeAsync(socket, CreateJoinWorld(character.CharacterId, sequence: 2));
    _ = await ReceiveServerEnvelopeAsync(socket);
    _ = await ReceiveServerEnvelopeAsync(socket);

    var before = fixture.SessionManager.GetLease(character.CharacterId)?.ExpiresAtUtc;
    await Task.Delay(25);
    await SendEnvelopeAsync(socket, CreateHeartbeat(sequence: 3));
    var heartbeat = await ReceiveServerEnvelopeAsync(socket);
    var after = fixture.SessionManager.GetLease(character.CharacterId)?.ExpiresAtUtc;
    await CloseNormalAsync(socket);

    return heartbeat.ServerError.Code == ErrorCode.HeartbeatAck
        && before is not null
        && after is not null
        && after > before;
}

static async Task<bool> ConcurrentJoinHasSingleOwnerAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-concurrent-join");
    var accountId = "account-vs007-concurrent";
    var character = await fixture.CreateKnightAsync(accountId, "Sir Session Race");
    var ticketOne = await fixture.IssueTicketAsync(accountId, "nonce-vs007-concurrent-1");
    var ticketTwo = await fixture.IssueTicketAsync(accountId, "nonce-vs007-concurrent-2");

    using var socketOne = await fixture.ConnectAsync();
    using var socketTwo = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socketOne, CreateHello(ticketOne, "nonce-vs007-concurrent-1", sequence: 1));
    await SendEnvelopeAsync(socketTwo, CreateHello(ticketTwo, "nonce-vs007-concurrent-2", sequence: 1));
    _ = await ReceiveServerEnvelopeAsync(socketOne);
    _ = await ReceiveServerEnvelopeAsync(socketTwo);

    await Task.WhenAll(
        SendEnvelopeAsync(socketOne, CreateJoinWorld(character.CharacterId, sequence: 2)),
        SendEnvelopeAsync(socketTwo, CreateJoinWorld(character.CharacterId, sequence: 2)));

    var responses = await Task.WhenAll(ReceiveServerEnvelopeAsync(socketOne), ReceiveServerEnvelopeAsync(socketTwo));
    await CloseNormalAsync(socketOne);
    await CloseNormalAsync(socketTwo);

    return responses.Count(response => response.PayloadCase == ServerEnvelope.PayloadOneofCase.JoinAccepted) == 1
        && responses.Count(response => response.PayloadCase == ServerEnvelope.PayloadOneofCase.ServerError
            && response.ServerError.Code == ErrorCode.SessionLeaseConflict) == 1
        && fixture.SessionManager.SnapshotEvents().Count(evt => evt.Kind == "join_accepted" && evt.CharacterId == character.CharacterId) == 1;
}

static async Task<bool> NormalDisconnectRecordsEventAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-disconnect");
    var accountId = "account-vs007-disconnect";
    var nonce = "nonce-vs007-disconnect";
    var character = await fixture.CreateKnightAsync(accountId, "Sir Session Leave");
    var ticket = await fixture.IssueTicketAsync(accountId, nonce);

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await SendEnvelopeAsync(socket, CreateJoinWorld(character.CharacterId, sequence: 2));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await CloseNormalAsync(socket);

    return await WaitUntilAsync(() =>
        fixture.SessionManager.GetLease(character.CharacterId) is null
        && fixture.SessionManager.SnapshotEvents().Any(evt => evt.Kind == "disconnect_normal" && evt.CharacterId == character.CharacterId));
}

static async Task<bool> LogsOmitSecretsAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs007-log-safety");
    var accountId = "account-vs007-log";
    var nonce = "nonce-vs007-log";
    var character = await fixture.CreateKnightAsync(accountId, "Sir Session Log");
    var ticket = await fixture.IssueTicketAsync(accountId, nonce);

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, nonce, sequence: 1));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await SendEnvelopeAsync(socket, CreateJoinWorld(character.CharacterId, sequence: 2));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await CloseNormalAsync(socket);
    _ = await WaitUntilAsync(() => fixture.Logs.Messages.Any(message => message.Contains("event=disconnect", StringComparison.Ordinal)));

    var messages = fixture.Logs.Messages.ToArray();
    return messages.Length > 0
        && messages.Any(message => message.Contains("connection_id=", StringComparison.Ordinal)
            && message.Contains("account_pseudonym=", StringComparison.Ordinal)
            && message.Contains("error_code=", StringComparison.Ordinal))
        && messages.All(message => !message.Contains(ticket, StringComparison.Ordinal))
        && messages.All(message => !message.Contains(accountId, StringComparison.Ordinal));
}

static async Task<bool> HandshakeErrorAsync(
    Func<GatewayFixture, Task<string>> ticketFactory,
    Func<string, ClientEnvelope> helloFactory,
    ErrorCode expectedCode,
    string fixtureName)
{
    await using var fixture = await GatewayFixture.StartAsync(fixtureName);
    var ticket = await ticketFactory(fixture);
    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, helloFactory(ticket));
    var response = await ReceiveServerEnvelopeAsync(socket);
    await WaitForCloseAsync(socket);
    return response.ServerError.Code == expectedCode;
}

static ClientEnvelope CreateHello(
    string ticket,
    string nonce,
    string buildId = GameTicketDefaults.DefaultBuildId,
    uint protocolVersion = ProtocolConstants.SupportedProtocolVersion,
    ulong sequence = 1) =>
    new()
    {
        ProtocolVersion = protocolVersion,
        Sequence = sequence,
        ClientTick = sequence,
        ClientHello = new ClientHello
        {
            BuildId = buildId,
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
            RequestedMapId = GatewaySessionDefaults.StubMapId,
            ContentHash = GatewaySessionDefaults.StubContentHash
        }
    };

static ClientEnvelope CreateHeartbeat(ulong sequence) =>
    new()
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = sequence,
        ClientTick = sequence,
        Heartbeat = new Heartbeat { ClientTimeMs = sequence }
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

static async Task WaitForCloseAsync(ClientWebSocket socket)
{
    var buffer = new byte[16];
    for (var attempt = 0; attempt < 20 && socket.State is WebSocketState.Open or WebSocketState.CloseSent; attempt++)
    {
        try
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
        }
        catch (WebSocketException)
        {
            return;
        }
    }
}

static async Task<bool> WaitUntilAsync(Func<bool> predicate)
{
    for (var attempt = 0; attempt < 40; attempt++)
    {
        if (predicate())
        {
            return true;
        }

        await Task.Delay(25);
    }

    return predicate();
}

static async Task<SessionCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new SessionCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new SessionCheck(name, false);
    }
}

internal readonly record struct SessionCheck(string Name, bool Passed);

internal sealed class GatewayFixture : IAsyncDisposable
{
    private readonly string? _previousStorePath;
    private readonly string? _previousCharacterStorePath;

    private GatewayFixture(
        WebApplication app,
        string httpBaseUrl,
        string storePath,
        string characterStorePath,
        string? previousStorePath,
        string? previousCharacterStorePath,
        CapturingLoggerProvider logs)
    {
        App = app;
        HttpBaseUrl = httpBaseUrl;
        StorePath = storePath;
        CharacterStorePath = characterStorePath;
        _previousStorePath = previousStorePath;
        _previousCharacterStorePath = previousCharacterStorePath;
        Store = new FileGameTicketStore(storePath);
        TicketService = new GameTicketService(Store);
        CharacterStore = new FileCharacterStore(characterStorePath);
        CharacterService = new CharacterService(CharacterStore);
        Logs = logs;
    }

    public WebApplication App { get; }
    public string HttpBaseUrl { get; }
    public string StorePath { get; }
    public string CharacterStorePath { get; }
    public FileGameTicketStore Store { get; }
    public GameTicketService TicketService { get; }
    public FileCharacterStore CharacterStore { get; }
    public CharacterService CharacterService { get; }
    public CapturingLoggerProvider Logs { get; }
    public GatewaySessionManager SessionManager => App.Services.GetRequiredService<GatewaySessionManager>();

    public static async Task<GatewayFixture> StartAsync(string name)
    {
        var storePath = TestPaths.CreateTempDirectory(name);
        var characterStorePath = TestPaths.CreateTempDirectory(name + "-characters");
        var previousStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
        var previousCharacterStorePath = Environment.GetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH");
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", storePath);
        Environment.SetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH", characterStorePath);

        var logs = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);

        var app = GatewayApp.Build(builder);
        var httpBaseUrl = $"http://127.0.0.1:{TestPaths.GetFreeTcpPort()}";
        app.Urls.Add(httpBaseUrl);
        await app.StartAsync();

        return new GatewayFixture(app, httpBaseUrl, storePath, characterStorePath, previousStorePath, previousCharacterStorePath, logs);
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
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", _previousStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH", _previousCharacterStorePath);
        TestPaths.DeleteDirectory(StorePath);
        TestPaths.DeleteDirectory(CharacterStorePath);
        Logs.Dispose();
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

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IEnumerable<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ConcurrentQueue<string> _messages;

        public CapturingLogger(string categoryName, ConcurrentQueue<string> messages)
        {
            _categoryName = categoryName;
            _messages = messages;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue($"{logLevel} {_categoryName} {formatter(state, exception)}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
