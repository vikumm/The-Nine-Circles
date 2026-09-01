using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway;
using Divinity.GameGateway.Protocol;
using Divinity.PlatformApi;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var checks = new List<GameTicketCheck>();

checks.Add(await CheckAsync("authenticated Platform API user can issue ticket", AuthenticatedPlatformApiIssueAsync));
checks.Add(await CheckAsync("Platform API rejects issue without authenticated user", PlatformApiRejectsUnauthenticatedIssueAsync));
checks.Add(await CheckAsync("ticket consumes only once", TicketConsumesOnlyOnceAsync));
checks.Add(await CheckAsync("two simultaneous consumes have one winner", ConcurrentConsumesHaveSingleWinnerAsync));
checks.Add(await CheckAsync("ticket expires after TTL", TicketExpiresAfterTtlAsync));
checks.Add(await CheckAsync("build mismatch is rejected", BuildMismatchRejectedAsync));
checks.Add(await CheckAsync("protocol_version mismatch is rejected", ProtocolMismatchRejectedAsync));
checks.Add(await CheckAsync("malformed ticket is rejected by gateway", GatewayRejectsMalformedTicketAsync));
checks.Add(await CheckAsync("gateway consumes valid ClientHello ticket", GatewayConsumesValidTicketAsync));
checks.Add(await CheckAsync("audit logs and store omit ticket secret", LogsAndStoreOmitTicketSecretAsync));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-006 game ticket tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-006 game ticket tests passed.");
return 0;

static async Task<bool> AuthenticatedPlatformApiIssueAsync()
{
    var storePath = TestPaths.CreateTempDirectory("vs006-platform-authenticated");
    var previousStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
    var previousDevHeader = Environment.GetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER");
    Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", storePath);
    Environment.SetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER", "false");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        EnvironmentName = Environments.Development,
        ContentRootPath = Directory.GetCurrentDirectory()
    });
    builder.Services.AddSingleton<IStartupFilter>(new TestAccountStartupFilter("account-vs006"));

    await using var app = PlatformApiApp.Build(builder);
    var url = $"http://127.0.0.1:{GetFreeTcpPort()}";
    app.Urls.Add(url);
    await app.StartAsync();

    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        using var response = await client.PostAsJsonAsync("/launcher/game-ticket", CreateIssueRequest());
        var body = await response.Content.ReadFromJsonAsync<GameTicketIssueHttpResponse>();

        return response.StatusCode == HttpStatusCode.OK
            && body is not null
            && GameTicketSecret.IsWellFormed(body.GameTicket)
            && body.TtlSeconds == 30;
    }
    finally
    {
        await app.StopAsync();
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", previousStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER", previousDevHeader);
        TestPaths.DeleteDirectory(storePath);
    }
}

static async Task<bool> PlatformApiRejectsUnauthenticatedIssueAsync()
{
    var storePath = TestPaths.CreateTempDirectory("vs006-platform-unauthenticated");
    var previousStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
    var previousDevHeader = Environment.GetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER");
    Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", storePath);
    Environment.SetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER", "false");

    await using var app = PlatformApiApp.Build(WebApplication.CreateBuilder(new WebApplicationOptions
    {
        EnvironmentName = Environments.Development,
        ContentRootPath = Directory.GetCurrentDirectory()
    }));
    var url = $"http://127.0.0.1:{GetFreeTcpPort()}";
    app.Urls.Add(url);
    await app.StartAsync();

    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        using var response = await client.PostAsJsonAsync("/launcher/game-ticket", CreateIssueRequest());
        return response.StatusCode == HttpStatusCode.Unauthorized;
    }
    finally
    {
        await app.StopAsync();
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", previousStorePath);
        Environment.SetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER", previousDevHeader);
        TestPaths.DeleteDirectory(storePath);
    }
}

static async Task<bool> TicketConsumesOnlyOnceAsync()
{
    using var fixture = new TicketFixture("vs006-consume-once");
    var issue = await fixture.Service.IssueAsync(CreateIssueCommand("account-once"), CancellationToken.None);
    var first = await fixture.Service.ConsumeAsync(CreateConsumeCommand(issue.GameTicket!), CancellationToken.None);
    var second = await fixture.Service.ConsumeAsync(CreateConsumeCommand(issue.GameTicket!), CancellationToken.None);

    return issue.Success
        && first.Status == GameTicketConsumeStatus.Consumed
        && second.Status == GameTicketConsumeStatus.ReusedTicket;
}

static async Task<bool> ConcurrentConsumesHaveSingleWinnerAsync()
{
    using var fixture = new TicketFixture("vs006-race");
    var issue = await fixture.Service.IssueAsync(CreateIssueCommand("account-race"), CancellationToken.None);
    if (!issue.Success)
    {
        return false;
    }

    var consumeTasks = Enumerable.Range(0, 2)
        .Select(_ => fixture.Service.ConsumeAsync(CreateConsumeCommand(issue.GameTicket!), CancellationToken.None))
        .ToArray();

    var results = await Task.WhenAll(consumeTasks);
    return results.Count(result => result.Status == GameTicketConsumeStatus.Consumed) == 1
        && results.Count(result => result.Status == GameTicketConsumeStatus.ReusedTicket) == 1;
}

static async Task<bool> TicketExpiresAfterTtlAsync()
{
    using var fixture = new TicketFixture("vs006-expired");
    var expiredTicket = GameTicketSecret.Create();
    var expiredRecord = new StoredGameTicket(
        "account-expired",
        GameTicketDefaults.DefaultBuildId,
        ProtocolConstants.SupportedProtocolVersion,
        "nonce-vs006",
        DateTimeOffset.UtcNow.AddMinutes(-2),
        DateTimeOffset.UtcNow.AddSeconds(-1));

    await fixture.Store.StoreAsync(GameTicketSecret.Hash(expiredTicket), expiredRecord, GameTicketDefaults.TimeToLive, CancellationToken.None);
    var result = await fixture.Service.ConsumeAsync(CreateConsumeCommand(expiredTicket), CancellationToken.None);

    return result.Status == GameTicketConsumeStatus.ExpiredTicket;
}

static async Task<bool> BuildMismatchRejectedAsync()
{
    using var fixture = new TicketFixture("vs006-build-mismatch");
    var issue = await fixture.Service.IssueAsync(CreateIssueCommand("account-build"), CancellationToken.None);
    var result = await fixture.Service.ConsumeAsync(
        new GameTicketConsumeCommand(issue.GameTicket!, "wrong-build", ProtocolConstants.SupportedProtocolVersion, "nonce-vs006"),
        CancellationToken.None);

    return result.Status == GameTicketConsumeStatus.BuildMismatch;
}

static async Task<bool> ProtocolMismatchRejectedAsync()
{
    using var fixture = new TicketFixture("vs006-protocol-mismatch");
    var issue = await fixture.Service.IssueAsync(CreateIssueCommand("account-protocol"), CancellationToken.None);
    var result = await fixture.Service.ConsumeAsync(
        new GameTicketConsumeCommand(issue.GameTicket!, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion + 1, "nonce-vs006"),
        CancellationToken.None);

    return result.Status == GameTicketConsumeStatus.ProtocolMismatch;
}

static async Task<bool> GatewayRejectsMalformedTicketAsync()
{
    using var fixture = new TicketFixture("vs006-gateway-malformed");
    var response = await ProtocolV1Handler.HandleClientEnvelopeAsync(
        CreateEnvelope("not-a-valid-ticket").ToByteArray(),
        fixture.Service,
        CancellationToken.None);

    return response.StatusCode == HttpStatusCode.BadRequest
        && response.Envelope.ServerError.Code == ErrorCode.GameTicketMalformed;
}

static async Task<bool> GatewayConsumesValidTicketAsync()
{
    var storePath = TestPaths.CreateTempDirectory("vs006-gateway-valid");
    var previousStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
    Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", storePath);

    var store = new FileGameTicketStore(storePath);
    var service = new GameTicketService(store);
    var issue = await service.IssueAsync(CreateIssueCommand("account-gateway"), CancellationToken.None);
    if (!issue.Success)
    {
        return false;
    }

    await using var app = GatewayApp.Build(WebApplication.CreateBuilder(new WebApplicationOptions
    {
        EnvironmentName = Environments.Development,
        ContentRootPath = Directory.GetCurrentDirectory()
    }));
    var url = $"http://127.0.0.1:{GetFreeTcpPort()}";
    app.Urls.Add(url);
    await app.StartAsync();

    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        using var content = new ByteArrayContent(CreateEnvelope(issue.GameTicket!).ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        using var response = await client.PostAsync("/protocol/v1/client-hello", content);
        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        var envelope = ServerEnvelope.Parser.ParseFrom(responseBytes);

        return response.StatusCode == HttpStatusCode.OK
            && envelope.ServerError.Code == ErrorCode.ClientHelloAcceptedNoSession;
    }
    finally
    {
        await app.StopAsync();
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", previousStorePath);
        TestPaths.DeleteDirectory(storePath);
    }
}

static async Task<bool> LogsAndStoreOmitTicketSecretAsync()
{
    using var fixture = new TicketFixture("vs006-log-safety");
    var issue = await fixture.Service.IssueAsync(CreateIssueCommand("account-log"), CancellationToken.None);
    _ = await fixture.Service.ConsumeAsync(CreateConsumeCommand(issue.GameTicket!), CancellationToken.None);

    var filesContainSecret = Directory.EnumerateFiles(fixture.StorePath, "*.json", SearchOption.AllDirectories)
        .Select(File.ReadAllText)
        .Any(content => content.Contains(issue.GameTicket!, StringComparison.Ordinal));
    var logsContainSecret = fixture.Audit.Entries.Any(entry => entry.Contains(issue.GameTicket!, StringComparison.Ordinal));

    return issue.Success && !filesContainSecret && !logsContainSecret;
}

static GameTicketIssueHttpRequest CreateIssueRequest() =>
    new(GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, "nonce-vs006");

static GameTicketIssueCommand CreateIssueCommand(string accountId) =>
    new(accountId, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, "nonce-vs006");

static GameTicketConsumeCommand CreateConsumeCommand(string ticket) =>
    new(ticket, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, "nonce-vs006");

static ClientEnvelope CreateEnvelope(string ticket) => new()
{
    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
    Sequence = 606,
    ClientTick = 1,
    ClientHello = new ClientHello
    {
        BuildId = GameTicketDefaults.DefaultBuildId,
        GameTicket = ticket,
        ClientNonce = "nonce-vs006"
    }
};

static async Task<GameTicketCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new GameTicketCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new GameTicketCheck(name, false);
    }
}

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

internal readonly record struct GameTicketCheck(string Name, bool Passed);

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
                    authenticationType: "VS-006 test"));

                await proceed();
            });

            next(app);
        };
}

internal sealed class TicketFixture : IDisposable
{
    public TicketFixture(string name)
    {
        StorePath = TestPaths.CreateTempDirectory(name);
        Store = new FileGameTicketStore(StorePath);
        Audit = new InMemoryGameTicketAuditSink();
        Service = new GameTicketService(Store, audit: Audit);
    }

    public string StorePath { get; }
    public FileGameTicketStore Store { get; }
    public InMemoryGameTicketAuditSink Audit { get; }
    public GameTicketService Service { get; }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}
