using System.Net;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Google.Protobuf;

namespace Divinity.GameGateway.Protocol;

public static class ProtocolV1Handler
{
    public static async Task<BoundedBody> ReadBoundedBodyAsync(Stream body, long? contentLength, CancellationToken cancellationToken)
    {
        if (contentLength > ProtocolConstants.MaxEnvelopeBytes)
        {
            return BoundedBody.TooLargeBody();
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var bytesRead = await body.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                return BoundedBody.Valid(buffer.ToArray());
            }

            if (buffer.Length + bytesRead > ProtocolConstants.MaxEnvelopeBytes)
            {
                return BoundedBody.TooLargeBody();
            }

            buffer.Write(chunk, 0, bytesRead);
        }
    }

    public static ProtocolResponse HandleClientEnvelope(byte[] payload)
    {
        return HandleClientEnvelopeAsync(payload, ticketService: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task<ProtocolResponse> HandleClientEnvelopeAsync(byte[] payload, GameTicketService? ticketService, CancellationToken cancellationToken)
    {
        if (payload.Length > ProtocolConstants.MaxEnvelopeBytes)
        {
            return CreatePayloadTooLargeResponse();
        }

        ClientEnvelope envelope;

        try
        {
            envelope = ClientEnvelope.Parser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return Error(HttpStatusCode.BadRequest, ErrorCode.MalformedPayload, "Malformed Protobuf ClientEnvelope.", 0);
        }

        if (envelope.ProtocolVersion != ProtocolConstants.SupportedProtocolVersion)
        {
            return Error(
                HttpStatusCode.BadRequest,
                ErrorCode.UnsupportedProtocolVersion,
                "Unsupported protocol_version.",
                envelope.Sequence);
        }

        if (envelope.PayloadCase != ClientEnvelope.PayloadOneofCase.ClientHello)
        {
            return Error(
                HttpStatusCode.BadRequest,
                ErrorCode.UnknownPayloadType,
                "VS-003 smoke endpoint accepts only ClientHello.",
                envelope.Sequence);
        }

        if (ticketService is not null)
        {
            var ticketResponse = await ValidateGameTicketAsync(envelope, ticketService, cancellationToken);
            if (ticketResponse is not null)
            {
                return ticketResponse;
            }
        }

        return Error(
            HttpStatusCode.OK,
            ErrorCode.ClientHelloAcceptedNoSession,
            ticketService is null
                ? "ClientHello accepted by VS-003 protocol smoke. Authenticated WSS session is VS-007."
                : "Game ticket consumed by VS-006. Authenticated WSS session is VS-007.",
            envelope.Sequence);
    }

    public static ProtocolResponse CreatePayloadTooLargeResponse() =>
        Error(HttpStatusCode.RequestEntityTooLarge, ErrorCode.PayloadTooLarge, "ClientEnvelope exceeds the 64 KiB limit.", 0);

    private static async Task<ProtocolResponse?> ValidateGameTicketAsync(ClientEnvelope envelope, GameTicketService ticketService, CancellationToken cancellationToken)
    {
        var hello = envelope.ClientHello;
        if (string.IsNullOrWhiteSpace(hello.GameTicket))
        {
            return Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketRequired, "ClientHello requires a game ticket.", envelope.Sequence);
        }

        var consumeResult = await ticketService.ConsumeAsync(
            new GameTicketConsumeCommand(
                hello.GameTicket,
                hello.BuildId,
                envelope.ProtocolVersion,
                hello.ClientNonce),
            cancellationToken);

        return consumeResult.Status switch
        {
            GameTicketConsumeStatus.Consumed => null,
            GameTicketConsumeStatus.MalformedTicket => Error(HttpStatusCode.BadRequest, ErrorCode.GameTicketMalformed, consumeResult.Message, envelope.Sequence),
            GameTicketConsumeStatus.ExpiredTicket => Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketExpired, consumeResult.Message, envelope.Sequence),
            GameTicketConsumeStatus.ReusedTicket => Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketReused, consumeResult.Message, envelope.Sequence),
            GameTicketConsumeStatus.BuildMismatch => Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketBuildMismatch, consumeResult.Message, envelope.Sequence),
            GameTicketConsumeStatus.ProtocolMismatch => Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketProtocolMismatch, consumeResult.Message, envelope.Sequence),
            GameTicketConsumeStatus.NonceMismatch => Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketNonceMismatch, consumeResult.Message, envelope.Sequence),
            _ => Error(HttpStatusCode.Unauthorized, ErrorCode.GameTicketInvalid, consumeResult.Message, envelope.Sequence)
        };
    }

    private static ProtocolResponse Error(HttpStatusCode statusCode, ErrorCode code, string message, ulong ackSequence) =>
        new(statusCode, new ServerEnvelope
        {
            ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
            ServerTick = 0,
            AckSequence = ackSequence,
            ServerError = new ServerError
            {
                Code = code,
                Message = message,
                CorrelationId = code == ErrorCode.ClientHelloAcceptedNoSession ? "vs003-protocol-smoke" : "vs006-game-ticket"
            }
        });
}

public sealed record BoundedBody(bool TooLarge, byte[] Payload)
{
    public static BoundedBody Valid(byte[] payload) => new(false, payload);
    public static BoundedBody TooLargeBody() => new(true, Array.Empty<byte>());
}

public sealed record ProtocolResponse(HttpStatusCode StatusCode, ServerEnvelope Envelope);
