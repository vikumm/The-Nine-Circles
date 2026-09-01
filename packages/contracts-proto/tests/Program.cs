using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway;
using Divinity.GameGateway.Protocol;
using Divinity.WorldRuntime;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;

var checks = new List<ProtocolCheck>();

checks.Add(Check("contracts compile for world runtime", WorldRuntimeInfo.UsesContractsProto));
checks.Add(Check("client envelope round-trips", ClientEnvelopeRoundTrips()));
checks.Add(Check("server envelope round-trips", ServerEnvelopeRoundTrips()));
checks.Add(Check("invalid protocol version rejected", InvalidProtocolVersionIsRejected()));
checks.Add(Check("payload over 64 KiB rejected", OversizedPayloadIsRejected()));
checks.Add(Check("truncated payload rejected", TruncatedPayloadIsRejected()));
checks.Add(Check("unknown payload type rejected", UnknownPayloadTypeIsRejected()));
checks.Add(await CheckAsync("client to gateway ClientHello smoke", ClientHelloSmokeAsync()));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"Protocol tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-003 protocol tests passed.");
return 0;

static bool ClientEnvelopeRoundTrips()
{
    var envelope = CreateClientHelloEnvelope();
    var parsed = ClientEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.ProtocolVersion == ProtocolConstants.SupportedProtocolVersion
        && parsed.Sequence == 42
        && parsed.ClientTick == 1234
        && parsed.PayloadCase == ClientEnvelope.PayloadOneofCase.ClientHello
        && parsed.ClientHello.BuildId == "vs003-smoke"
        && parsed.ClientHello.GameTicket == "ticket-for-contract-test";
}

static bool ServerEnvelopeRoundTrips()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        ServerTick = 9,
        AckSequence = 42,
        ServerError = new ServerError
        {
            Code = ErrorCode.ClientHelloAcceptedNoSession,
            Message = "VS-003 controlled response",
            CorrelationId = "test-correlation"
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.ProtocolVersion == ProtocolConstants.SupportedProtocolVersion
        && parsed.ServerTick == 9
        && parsed.AckSequence == 42
        && parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.ServerError
        && parsed.ServerError.Code == ErrorCode.ClientHelloAcceptedNoSession;
}

static bool InvalidProtocolVersionIsRejected()
{
    var envelope = CreateClientHelloEnvelope();
    envelope.ProtocolVersion = ProtocolConstants.SupportedProtocolVersion + 1;

    var result = ProtocolV1Handler.HandleClientEnvelope(envelope.ToByteArray());

    return result.StatusCode == HttpStatusCode.BadRequest
        && result.Envelope.ServerError.Code == ErrorCode.UnsupportedProtocolVersion;
}

static bool TruncatedPayloadIsRejected()
{
    var payload = CreateClientHelloEnvelope().ToByteArray();
    var truncated = payload.Take(payload.Length - 1).ToArray();

    var result = ProtocolV1Handler.HandleClientEnvelope(truncated);

    return result.StatusCode == HttpStatusCode.BadRequest
        && result.Envelope.ServerError.Code == ErrorCode.MalformedPayload;
}

static bool OversizedPayloadIsRejected()
{
    var payload = new byte[ProtocolConstants.MaxEnvelopeBytes + 1];

    var result = ProtocolV1Handler.HandleClientEnvelope(payload);

    return result.StatusCode == HttpStatusCode.RequestEntityTooLarge
        && result.Envelope.ServerError.Code == ErrorCode.PayloadTooLarge;
}

static bool UnknownPayloadTypeIsRejected()
{
    var envelope = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 99,
        ClientTick = 100
    };

    var result = ProtocolV1Handler.HandleClientEnvelope(envelope.ToByteArray());

    return result.StatusCode == HttpStatusCode.BadRequest
        && result.Envelope.ServerError.Code == ErrorCode.UnknownPayloadType;
}

static async Task<bool> ClientHelloSmokeAsync()
{
    var storePath = CreateTempDirectory("vs006-gateway-protocol-smoke");
    var previousStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
    Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", storePath);

    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    var app = GatewayApp.Build(builder);
    var port = GetFreeTcpPort();
    var url = $"http://127.0.0.1:{port}";
    var nonce = "nonce-for-contract-test";
    var issueResult = await new GameTicketService(new FileGameTicketStore(storePath)).IssueAsync(
        new GameTicketIssueCommand("account-protocol-smoke", "vs003-smoke", ProtocolConstants.SupportedProtocolVersion, nonce),
        CancellationToken.None);

    app.Urls.Add(url);
    await app.StartAsync();

    try
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(url)
        };

        if (!issueResult.Success)
        {
            return false;
        }

        var requestBytes = CreateClientHelloEnvelope(issueResult.GameTicket!, nonce).ToByteArray();
        using var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await client.PostAsync("/protocol/v1/client-hello", content);
        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        var envelope = ServerEnvelope.Parser.ParseFrom(responseBytes);

        return response.StatusCode == HttpStatusCode.OK
            && envelope.ProtocolVersion == ProtocolConstants.SupportedProtocolVersion
            && envelope.AckSequence == 42
            && envelope.PayloadCase == ServerEnvelope.PayloadOneofCase.ServerError
            && envelope.ServerError.Code == ErrorCode.ClientHelloAcceptedNoSession;
    }
    finally
    {
        await app.StopAsync();
        await app.DisposeAsync();
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", previousStorePath);
        DeleteDirectory(storePath);
    }
}

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static ClientEnvelope CreateClientHelloEnvelope(string gameTicket = "ticket-for-contract-test", string nonce = "nonce-for-contract-test") => new()
{
    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
    Sequence = 42,
    ClientTick = 1234,
    ClientHello = new ClientHello
    {
        BuildId = "vs003-smoke",
        GameTicket = gameTicket,
        ClientNonce = nonce
    }
};

static string CreateTempDirectory(string name)
{
    var path = Path.Combine(Path.GetTempPath(), "divinity", name, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void DeleteDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }
}

static ProtocolCheck Check(string name, bool passed) => new(name, passed);

static async Task<ProtocolCheck> CheckAsync(string name, Task<bool> check)
{
    try
    {
        return new ProtocolCheck(name, await check);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new ProtocolCheck(name, false);
    }
}

internal readonly record struct ProtocolCheck(string Name, bool Passed);
