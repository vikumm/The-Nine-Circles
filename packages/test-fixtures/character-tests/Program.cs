using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.PlatformApi;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var checks = new List<CharacterCheck>();

checks.Add(await CheckAsync("valid Knight creation", ValidKnightCreationAsync));
checks.Add(await CheckAsync("invalid name is rejected", InvalidNameRejectedAsync));
checks.Add(await CheckAsync("duplicate normalized name is rejected", DuplicateNormalizedNameRejectedAsync));
checks.Add(await CheckAsync("second creation in one slot is rejected", SecondCreationRejectedAsync));
checks.Add(await CheckAsync("existing Knight can be selected", ExistingKnightCanBeSelectedAsync));
checks.Add(await CheckAsync("JoinWorld without ownership is rejected", JoinWithoutOwnershipRejectedAsync));
checks.Add(await CheckAsync("character persists after store restart", PersistenceAfterRestartAsync));
checks.Add(await CheckAsync("initial snapshot uses server authority", InitialSnapshotUsesServerAuthorityAsync));
checks.Add(await CheckAsync("concurrent duplicate name has one winner", ConcurrentDuplicateNameHasOneWinnerAsync));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-008 character tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-008 character tests passed.");
return 0;

static async Task<bool> ValidKnightCreationAsync()
{
    await using var fixture = await PlatformFixture.StartAsync("vs008-create", "account-vs008-create");
    using var response = await fixture.Client.PostAsJsonAsync("/characters/knight", new CharacterCreateHttpRequest(" Sir Aster "));
    var body = await response.Content.ReadFromJsonAsync<CharacterHttpResponse>();

    return response.StatusCode == HttpStatusCode.Created
        && body is not null
        && body.DisplayName == "Sir Aster"
        && body.NormalizedName == "SIR ASTER"
        && body.Vocation == KnightCatalog.Vocation
        && body.CatalogVersion == KnightCatalog.CatalogVersion
        && body.Stats.Level == KnightCatalog.LevelOneStats.Level
        && body.Stats.MaxHp == KnightCatalog.LevelOneStats.MaxHp
        && body.Stats.MaxMp == KnightCatalog.LevelOneStats.MaxMp
        && body.SafeSpawn.X == KnightCatalog.SafeSpawn.X
        && body.SafeSpawn.Y == KnightCatalog.SafeSpawn.Y;
}

static async Task<bool> InvalidNameRejectedAsync()
{
    await using var fixture = await PlatformFixture.StartAsync("vs008-invalid-name", "account-vs008-invalid");
    using var response = await fixture.Client.PostAsJsonAsync("/characters/knight", new CharacterCreateHttpRequest("@@"));
    var body = await response.Content.ReadFromJsonAsync<CharacterHttpErrorResponse>();

    return response.StatusCode == HttpStatusCode.BadRequest
        && body is not null
        && body.Code == CharacterServiceStatus.InvalidName.ToString()
        && !string.IsNullOrWhiteSpace(body.Message);
}

static async Task<bool> DuplicateNormalizedNameRejectedAsync()
{
    using var fixture = new CharacterStoreFixture("vs008-duplicate-normalized");
    var first = await fixture.Service.CreateKnightAsync(new CreateKnightCommand("account-one", "Sir Vale"), CancellationToken.None);
    var second = await fixture.Service.CreateKnightAsync(new CreateKnightCommand("account-two", "  sir   vále "), CancellationToken.None);

    return first.Status == CharacterServiceStatus.Created
        && second.Status == CharacterServiceStatus.DuplicateName;
}

static async Task<bool> SecondCreationRejectedAsync()
{
    using var fixture = new CharacterStoreFixture("vs008-slot");
    var first = await fixture.Service.CreateKnightAsync(new CreateKnightCommand("account-slot", "Sir Slot"), CancellationToken.None);
    var second = await fixture.Service.CreateKnightAsync(new CreateKnightCommand("account-slot", "Sir Other"), CancellationToken.None);

    return first.Status == CharacterServiceStatus.Created
        && second.Status == CharacterServiceStatus.SlotOccupied;
}

static async Task<bool> ExistingKnightCanBeSelectedAsync()
{
    await using var fixture = await PlatformFixture.StartAsync("vs008-select", "account-vs008-select");
    using var create = await fixture.Client.PostAsJsonAsync("/characters/knight", new CharacterCreateHttpRequest("Sir Selected"));
    var created = await create.Content.ReadFromJsonAsync<CharacterHttpResponse>();

    using var select = await fixture.Client.GetAsync("/characters/knight");
    var selected = await select.Content.ReadFromJsonAsync<CharacterHttpResponse>();

    return create.StatusCode == HttpStatusCode.Created
        && select.StatusCode == HttpStatusCode.OK
        && created is not null
        && selected is not null
        && selected.CharacterId == created.CharacterId
        && selected.DisplayName == created.DisplayName;
}

static async Task<bool> JoinWithoutOwnershipRejectedAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs008-ownership");
    var ownerCharacter = await fixture.CreateKnightAsync("account-vs008-owner", "Sir Owner");
    var ticket = await fixture.IssueTicketAsync("account-vs008-intruder", "nonce-vs008-intruder");

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, "nonce-vs008-intruder", sequence: 1));
    var hello = await ReceiveServerEnvelopeAsync(socket);
    await SendEnvelopeAsync(socket, CreateJoinWorld(ownerCharacter.CharacterId, "client-map", "client-hash", sequence: 2));
    var join = await ReceiveServerEnvelopeAsync(socket);
    await CloseNormalAsync(socket);

    return hello.ServerError.Code == ErrorCode.ClientHelloAcceptedSession
        && join.PayloadCase == ServerEnvelope.PayloadOneofCase.ServerError
        && join.ServerError.Code == ErrorCode.CharacterNotOwned;
}

static async Task<bool> PersistenceAfterRestartAsync()
{
    var storePath = TestPaths.CreateTempDirectory("vs008-restart");
    try
    {
        var firstService = new CharacterService(new FileCharacterStore(storePath));
        var created = await firstService.CreateKnightAsync(new CreateKnightCommand("account-vs008-restart", "Sir Durable"), CancellationToken.None);
        var restartedService = new CharacterService(new FileCharacterStore(storePath));
        var selected = await restartedService.SelectKnightAsync("account-vs008-restart", CancellationToken.None);

        return created.Status == CharacterServiceStatus.Created
            && selected.Status == CharacterServiceStatus.Selected
            && selected.Character?.CharacterId == created.Character?.CharacterId;
    }
    finally
    {
        TestPaths.DeleteDirectory(storePath);
    }
}

static async Task<bool> InitialSnapshotUsesServerAuthorityAsync()
{
    await using var fixture = await GatewayFixture.StartAsync("vs008-snapshot");
    var accountId = "account-vs008-snapshot";
    var character = await fixture.CreateKnightAsync(accountId, "Sir Snapshot");
    var ticket = await fixture.IssueTicketAsync(accountId, "nonce-vs008-snapshot");

    using var socket = await fixture.ConnectAsync();
    await SendEnvelopeAsync(socket, CreateHello(ticket, "nonce-vs008-snapshot", sequence: 1));
    _ = await ReceiveServerEnvelopeAsync(socket);
    await SendEnvelopeAsync(socket, CreateJoinWorld(character.CharacterId, "map_training_field_01", KnightCatalog.ContentHash, sequence: 2));
    var join = await ReceiveServerEnvelopeAsync(socket);
    var snapshot = await ReceiveServerEnvelopeAsync(socket);
    await CloseNormalAsync(socket);

    var entity = snapshot.WorldSnapshot.Entities.SingleOrDefault(entity => entity.EntityId == character.CharacterId);
    return join.PayloadCase == ServerEnvelope.PayloadOneofCase.JoinAccepted
        && join.JoinAccepted.MapId == KnightCatalog.MapId
        && join.JoinAccepted.ContentHash == KnightCatalog.ContentHash
        && join.JoinAccepted.Position.X == (float)KnightCatalog.SafeSpawn.X
        && join.JoinAccepted.Position.Y == (float)KnightCatalog.SafeSpawn.Y
        && join.JoinAccepted.Stats.Level == KnightCatalog.LevelOneStats.Level
        && join.JoinAccepted.Stats.Hp == KnightCatalog.LevelOneStats.MaxHp
        && join.JoinAccepted.Stats.Mp == KnightCatalog.LevelOneStats.MaxMp
        && snapshot.PayloadCase == ServerEnvelope.PayloadOneofCase.WorldSnapshot
        && snapshot.WorldSnapshot.MapId == KnightCatalog.MapId
        && entity is not null
        && entity.Position.X == (float)KnightCatalog.SafeSpawn.X
        && entity.Position.Y == (float)KnightCatalog.SafeSpawn.Y
        && entity.Level == KnightCatalog.LevelOneStats.Level
        && entity.Hp == KnightCatalog.LevelOneStats.MaxHp
        && entity.Mp == KnightCatalog.LevelOneStats.MaxMp;
}

static async Task<bool> ConcurrentDuplicateNameHasOneWinnerAsync()
{
    using var fixture = new CharacterStoreFixture("vs008-concurrent-name");
    var tasks = new[]
    {
        fixture.Service.CreateKnightAsync(new CreateKnightCommand("account-race-one", "Sir Race"), CancellationToken.None),
        fixture.Service.CreateKnightAsync(new CreateKnightCommand("account-race-two", " sir   race "), CancellationToken.None)
    };

    var results = await Task.WhenAll(tasks);
    return results.Count(result => result.Status == CharacterServiceStatus.Created) == 1
        && results.Count(result => result.Status == CharacterServiceStatus.DuplicateName) == 1;
}

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

static ClientEnvelope CreateJoinWorld(string characterId, string requestedMapId, string contentHash, ulong sequence) =>
    new()
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = sequence,
        ClientTick = sequence,
        JoinWorld = new JoinWorld
        {
            CharacterId = characterId,
            RequestedMapId = requestedMapId,
            ContentHash = contentHash
        }
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

static async Task<CharacterCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new CharacterCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new CharacterCheck(name, false);
    }
}

internal readonly record struct CharacterCheck(string Name, bool Passed);

internal sealed class CharacterStoreFixture : IDisposable
{
    public CharacterStoreFixture(string name)
    {
        StorePath = TestPaths.CreateTempDirectory(name);
        Store = new FileCharacterStore(StorePath);
        Service = new CharacterService(Store);
    }

    public string StorePath { get; }
    public FileCharacterStore Store { get; }
    public CharacterService Service { get; }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}

internal sealed class PlatformFixture : IAsyncDisposable
{
    private readonly string? _previousCharacterStorePath;
    private readonly string? _previousTicketStorePath;

    private PlatformFixture(WebApplication app, HttpClient client, string characterStorePath, string ticketStorePath, string? previousCharacterStorePath, string? previousTicketStorePath)
    {
        App = app;
        Client = client;
        CharacterStorePath = characterStorePath;
        TicketStorePath = ticketStorePath;
        _previousCharacterStorePath = previousCharacterStorePath;
        _previousTicketStorePath = previousTicketStorePath;
    }

    public WebApplication App { get; }
    public HttpClient Client { get; }
    public string CharacterStorePath { get; }
    public string TicketStorePath { get; }

    public static async Task<PlatformFixture> StartAsync(string name, string accountId)
    {
        var characterStorePath = TestPaths.CreateTempDirectory(name + "-characters");
        var ticketStorePath = TestPaths.CreateTempDirectory(name + "-tickets");
        var previousCharacterStorePath = Environment.GetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH");
        var previousTicketStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
        Environment.SetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH", characterStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", ticketStorePath);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        builder.Services.AddSingleton<IStartupFilter>(new TestAccountStartupFilter(accountId));

        var app = PlatformApiApp.Build(builder);
        var baseUrl = $"http://127.0.0.1:{TestPaths.GetFreeTcpPort()}";
        app.Urls.Add(baseUrl);
        await app.StartAsync();

        return new PlatformFixture(app, new HttpClient { BaseAddress = new Uri(baseUrl) }, characterStorePath, ticketStorePath, previousCharacterStorePath, previousTicketStorePath);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        Environment.SetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH", _previousCharacterStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", _previousTicketStorePath);
        TestPaths.DeleteDirectory(CharacterStorePath);
        TestPaths.DeleteDirectory(TicketStorePath);
    }
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

internal sealed class TestAccountStartupFilter : IStartupFilter
{
    private readonly string _accountId;

    public TestAccountStartupFilter(string accountId)
    {
        _accountId = accountId;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, proceed) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, _accountId) },
                    authenticationType: "VS-008 test"));

                await proceed();
            });

            next(app);
        };
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
