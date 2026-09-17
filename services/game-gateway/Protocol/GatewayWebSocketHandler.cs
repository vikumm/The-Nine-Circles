using System.Net.WebSockets;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Movement;
using Divinity.WorldRuntime.Monsters;
using Google.Protobuf;

namespace Divinity.GameGateway.Protocol;

public sealed class GatewayWebSocketHandler
{
    private readonly GameTicketService _ticketService;
    private readonly CharacterService _characterService;
    private readonly WorldMovementRuntime _worldRuntime;
    private readonly WorldMonsterRuntime _monsterRuntime;
    private readonly WorldCombatRuntime _combatRuntime;
    private readonly GatewaySessionManager _sessionManager;
    private readonly ILogger<GatewayWebSocketHandler> _logger;

    public GatewayWebSocketHandler(
        GameTicketService ticketService,
        CharacterService characterService,
        WorldMovementRuntime worldRuntime,
        WorldMonsterRuntime monsterRuntime,
        WorldCombatRuntime combatRuntime,
        GatewaySessionManager sessionManager,
        ILogger<GatewayWebSocketHandler> logger)
    {
        _ticketService = ticketService;
        _characterService = characterService;
        _worldRuntime = worldRuntime;
        _monsterRuntime = monsterRuntime;
        _combatRuntime = combatRuntime;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task HandleAsync(WebSocket socket, string connectionId, CancellationToken cancellationToken)
    {
        GatewaySession? session = null;
        var disconnectReason = "disconnect_normal";

        try
        {
            var helloResult = await ReceiveEnvelopeAsync(socket, cancellationToken);
            if (!await EnsureEnvelopeAsync(socket, helloResult, connectionId, cancellationToken))
            {
                disconnectReason = helloResult.CloseReceived ? "disconnect_normal" : "disconnect_handshake_rejected";
                _logger.LogInformation(
                    "gateway_ws event=client_hello connection_id={ConnectionId} account_pseudonym=anonymous result={Result} error_code={ErrorCode}",
                    connectionId,
                    disconnectReason,
                    helloResult.ErrorCode ?? ErrorCode.Unspecified);
                return;
            }

            var helloEnvelope = helloResult.Envelope!;
            if (helloEnvelope.PayloadCase != ClientEnvelope.PayloadOneofCase.ClientHello)
            {
                await SendErrorAndCloseAsync(socket, ErrorCode.HandshakeRequired, "ClientHello is required as the first WSS message.", helloEnvelope.Sequence, connectionId, cancellationToken);
                disconnectReason = "disconnect_handshake_required";
                return;
            }

            var authResult = await AuthenticateClientHelloAsync(helloEnvelope, cancellationToken);
            if (!authResult.Success)
            {
                await SendErrorAndCloseAsync(socket, authResult.ErrorCode, authResult.Message, helloEnvelope.Sequence, connectionId, cancellationToken);
                disconnectReason = "disconnect_handshake_rejected";
                _logger.LogInformation(
                    "gateway_ws event=client_hello connection_id={ConnectionId} account_pseudonym=anonymous result=rejected error_code={ErrorCode}",
                    connectionId,
                    authResult.ErrorCode);
                return;
            }

            session = _sessionManager.CreateAuthenticatedSession(connectionId, authResult.Ticket!);
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.ClientHelloAcceptedSession, "ClientHello accepted; send JoinWorld to acquire a session lease.", helloEnvelope.Sequence, connectionId),
                cancellationToken);
            _logger.LogInformation(
                "gateway_ws event=client_hello connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=accepted error_code={ErrorCode}",
                connectionId,
                session.AccountPseudonym,
                ErrorCode.ClientHelloAcceptedSession);

            await ReceiveAuthenticatedMessagesAsync(socket, connectionId, session, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            disconnectReason = "disconnect_cancelled";
        }
        finally
        {
            if (session is not null)
            {
                if (!string.IsNullOrWhiteSpace(session.CharacterId))
                {
                    await _worldRuntime.DisconnectAsync(session.CharacterId, disconnectReason, CancellationToken.None);
                    _monsterRuntime.DisconnectPlayer(session.CharacterId);
                }

                _sessionManager.Disconnect(connectionId, disconnectReason);
                _logger.LogInformation(
                    "gateway_ws event=disconnect connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
                    connectionId,
                    session.AccountPseudonym,
                    disconnectReason,
                    ErrorCode.Unspecified);
            }
        }
    }

    private async Task ReceiveAuthenticatedMessagesAsync(WebSocket socket, string connectionId, GatewaySession session, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open)
        {
            var receiveResult = await ReceiveEnvelopeAsync(socket, cancellationToken);
            if (!await EnsureEnvelopeAsync(socket, receiveResult, connectionId, cancellationToken))
            {
                _logger.LogInformation(
                    "gateway_ws event=session_message connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=rejected error_code={ErrorCode}",
                    connectionId,
                    session.AccountPseudonym,
                    receiveResult.ErrorCode ?? ErrorCode.Unspecified);
                return;
            }

            var envelope = receiveResult.Envelope!;
            if (envelope.ProtocolVersion != ProtocolConstants.SupportedProtocolVersion)
            {
                await SendErrorAndCloseAsync(socket, ErrorCode.UnsupportedProtocolVersion, "Unsupported protocol_version.", envelope.Sequence, connectionId, cancellationToken);
                return;
            }

            switch (envelope.PayloadCase)
            {
                case ClientEnvelope.PayloadOneofCase.JoinWorld:
                    await HandleJoinWorldAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.MoveIntent:
                    await HandleMoveIntentAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.AttackIntent:
                    await HandleAttackIntentAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.Heartbeat:
                    await HandleHeartbeatAsync(socket, connectionId, session, envelope.Sequence, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.ReconnectRequest:
                    await SendEnvelopeAsync(
                        socket,
                        CreateServerErrorEnvelope(ErrorCode.ReconnectUnsupported, "ReconnectRequest is a VS-007 protocol stub; reconnect flow is not implemented yet.", envelope.Sequence, connectionId),
                        cancellationToken);
                    break;
                default:
                    await SendEnvelopeAsync(
                        socket,
                        CreateServerErrorEnvelope(ErrorCode.UnknownPayloadType, "Authenticated WSS accepts JoinWorld, MoveIntent, AttackIntent, Heartbeat and ReconnectRequest stub.", envelope.Sequence, connectionId),
                        cancellationToken);
                    break;
            }
        }
    }

    private async Task HandleJoinWorldAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        var join = envelope.JoinWorld;
        var characterResult = await _characterService.GetOwnedCharacterAsync(session.AccountId, join.CharacterId, cancellationToken);
        if (characterResult.Status != CharacterServiceStatus.Selected)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.CharacterNotOwned, characterResult.Message, envelope.Sequence, connectionId),
                cancellationToken);
            _logger.LogInformation(
                "gateway_ws event=join_world connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=rejected error_code={ErrorCode}",
                connectionId,
                session.AccountPseudonym,
                ErrorCode.CharacterNotOwned);
            return;
        }

        var character = characterResult.Character!;
        var mapResult = _worldRuntime.ValidateJoin(new WorldJoinMapRequest(join.RequestedMapId, join.ContentHash));
        if (!mapResult.Success)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(mapResult.ProtocolErrorCode, mapResult.Message, envelope.Sequence, connectionId),
                cancellationToken);
            _logger.LogInformation(
                "gateway_ws event=join_world connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=rejected error_code={ErrorCode}",
                connectionId,
                session.AccountPseudonym,
                mapResult.ProtocolErrorCode);
            return;
        }

        var result = _sessionManager.TryJoin(connectionId, character.CharacterId);
        if (result.Status == JoinLeaseStatus.Joined)
        {
            var worldJoin = await _worldRuntime.JoinAsync(character, cancellationToken);
            _monsterRuntime.UpsertPlayer(
                worldJoin.CharacterId,
                (double)worldJoin.Position.X,
                (double)worldJoin.Position.Y,
                hp: worldJoin.Stats.Hp);
            await SendEnvelopeAsync(
                socket,
                new ServerEnvelope
                {
                    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                    AckSequence = envelope.Sequence,
                    JoinAccepted = new JoinAccepted
                    {
                        CharacterId = worldJoin.CharacterId,
                        MapId = worldJoin.MapId,
                        ChannelId = worldJoin.ChannelId,
                        Position = ToVector2(worldJoin.Position),
                        Stats = worldJoin.Stats,
                        ContentHash = worldJoin.ContentHash
                    }
                },
                cancellationToken);
            await SendEnvelopeAsync(
                socket,
                CreateSnapshotEnvelope(worldJoin.Snapshot, envelope.Sequence),
                cancellationToken);

            _logger.LogInformation(
                "gateway_ws event=join_world connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=accepted error_code={ErrorCode}",
                connectionId,
                session.AccountPseudonym,
                ErrorCode.Unspecified);
            return;
        }

        var code = result.Status switch
        {
            JoinLeaseStatus.LeaseConflict => ErrorCode.SessionLeaseConflict,
            JoinLeaseStatus.MissingCharacterId => ErrorCode.CharacterNotOwned,
            _ => ErrorCode.HandshakeRequired
        };

        await SendEnvelopeAsync(socket, CreateServerErrorEnvelope(code, result.Message, envelope.Sequence, connectionId), cancellationToken);
        _logger.LogInformation(
            "gateway_ws event=join_world connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=rejected error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            code);
    }

    private async Task HandleMoveIntentAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.CharacterId))
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.SessionNotJoined, "JoinWorld is required before MoveIntent.", envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var rateLimit = _sessionManager.TryAcquireMoveIntent(connectionId);
        if (rateLimit.Status != MoveIntentRateLimitStatus.Accepted)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.MoveRejected, rateLimit.Message, envelope.Sequence, connectionId),
                cancellationToken);
            _logger.LogInformation(
                "gateway_ws event=move_intent connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
                connectionId,
                session.AccountPseudonym,
                rateLimit.Status,
                ErrorCode.MoveRejected);
            return;
        }

        var result = await _worldRuntime.ApplyMoveAsync(
            session.CharacterId,
            envelope.Sequence,
            envelope.ClientTick,
            envelope.MoveIntent,
            cancellationToken);

        if (result.Snapshot is not null)
        {
            _monsterRuntime.UpsertPlayer(
                session.CharacterId,
                (double)result.Position.X,
                (double)result.Position.Y);
            await SendEnvelopeAsync(socket, CreateSnapshotEnvelope(result.Snapshot, envelope.Sequence), cancellationToken);
        }
        else if (result.Accepted)
        {
            _monsterRuntime.UpsertPlayer(
                session.CharacterId,
                (double)result.Position.X,
                (double)result.Position.Y);
        }
        else if (result.Correction is not null)
        {
            await SendEnvelopeAsync(
                socket,
                new ServerEnvelope
                {
                    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                    AckSequence = envelope.Sequence,
                    Correction = result.Correction
                },
                cancellationToken);
        }
        else if (!result.Accepted)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(result.ErrorCode, result.Message, envelope.Sequence, connectionId),
                cancellationToken);
        }

        _logger.LogInformation(
            "gateway_ws event=move_intent connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            result.Status,
            result.ErrorCode);
    }

    private async Task HandleAttackIntentAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.CharacterId))
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.SessionNotJoined, "JoinWorld is required before AttackIntent.", envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var result = _combatRuntime.ApplyBasicAttack(session.CharacterId, envelope.Sequence, envelope.AttackIntent);
        if (result.CombatEvent is not null)
        {
            await SendEnvelopeAsync(
                socket,
                new ServerEnvelope
                {
                    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                    AckSequence = envelope.Sequence,
                    CombatEvent = result.CombatEvent
                },
                cancellationToken);
        }
        else if (result.SkillStateChanged is not null)
        {
            await SendEnvelopeAsync(
                socket,
                new ServerEnvelope
                {
                    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                    AckSequence = envelope.Sequence,
                    SkillStateChanged = result.SkillStateChanged
                },
                cancellationToken);
        }
        else
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(result.ErrorCode, result.Message, envelope.Sequence, connectionId),
                cancellationToken);
        }

        _logger.LogInformation(
            "gateway_ws event=attack_intent connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            result.Status,
            result.ErrorCode);
    }

    private async Task HandleHeartbeatAsync(WebSocket socket, string connectionId, GatewaySession session, ulong sequence, CancellationToken cancellationToken)
    {
        var result = _sessionManager.RenewHeartbeat(connectionId);
        var code = result.Status == HeartbeatLeaseStatus.Renewed
            ? ErrorCode.HeartbeatAck
            : result.Status == HeartbeatLeaseStatus.SessionNotJoined
                ? ErrorCode.SessionNotJoined
                : ErrorCode.HeartbeatRejected;

        await SendEnvelopeAsync(socket, CreateServerErrorEnvelope(code, result.Message, sequence, connectionId), cancellationToken);
        _logger.LogInformation(
            "gateway_ws event=heartbeat connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            result.Status == HeartbeatLeaseStatus.Renewed ? "renewed" : "rejected",
            code);
    }

    private async Task<ClientHelloAuthResult> AuthenticateClientHelloAsync(ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.ProtocolVersion != ProtocolConstants.SupportedProtocolVersion)
        {
            return ClientHelloAuthResult.Rejected(ErrorCode.UnsupportedProtocolVersion, "Unsupported protocol_version.");
        }

        var hello = envelope.ClientHello;
        if (string.IsNullOrWhiteSpace(hello.GameTicket))
        {
            return ClientHelloAuthResult.Rejected(ErrorCode.GameTicketRequired, "ClientHello requires a game ticket.");
        }

        var consumeResult = await _ticketService.ConsumeAsync(
            new GameTicketConsumeCommand(hello.GameTicket, hello.BuildId, envelope.ProtocolVersion, hello.ClientNonce),
            cancellationToken);

        return consumeResult.Status switch
        {
            GameTicketConsumeStatus.Consumed => ClientHelloAuthResult.Accepted(consumeResult.Ticket!),
            GameTicketConsumeStatus.MalformedTicket => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketMalformed, consumeResult.Message),
            GameTicketConsumeStatus.ExpiredTicket => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketExpired, consumeResult.Message),
            GameTicketConsumeStatus.ReusedTicket => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketReused, consumeResult.Message),
            GameTicketConsumeStatus.BuildMismatch => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketBuildMismatch, consumeResult.Message),
            GameTicketConsumeStatus.ProtocolMismatch => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketProtocolMismatch, consumeResult.Message),
            GameTicketConsumeStatus.NonceMismatch => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketNonceMismatch, consumeResult.Message),
            _ => ClientHelloAuthResult.Rejected(ErrorCode.GameTicketInvalid, consumeResult.Message)
        };
    }

    private static ServerEnvelope CreateSnapshotEnvelope(WorldSnapshot snapshot, ulong ackSequence) =>
        new()
        {
            ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
            AckSequence = ackSequence,
            WorldSnapshot = snapshot
        };

    private static Vector2 ToVector2(CharacterPosition position) =>
        new()
        {
            X = (float)position.X,
            Y = (float)position.Y
        };

    private static async Task<bool> EnsureEnvelopeAsync(WebSocket socket, WebSocketEnvelopeResult result, string connectionId, CancellationToken cancellationToken)
    {
        if (result.CloseReceived)
        {
            if (socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client closed", cancellationToken);
            }

            return false;
        }

        if (result.ErrorCode is null)
        {
            return true;
        }

        await SendErrorAndCloseAsync(socket, result.ErrorCode.Value, result.Message, 0, connectionId, cancellationToken);
        return false;
    }

    private static async Task<WebSocketEnvelopeResult> ReceiveEnvelopeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var payload = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return WebSocketEnvelopeResult.Close();
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                return WebSocketEnvelopeResult.Rejected(ErrorCode.MalformedPayload, "Only binary Protobuf WebSocket messages are accepted.");
            }

            if (payload.Length + result.Count > ProtocolConstants.MaxEnvelopeBytes)
            {
                return WebSocketEnvelopeResult.Rejected(ErrorCode.PayloadTooLarge, "ClientEnvelope exceeds the 64 KiB limit.");
            }

            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        try
        {
            return WebSocketEnvelopeResult.Valid(ClientEnvelope.Parser.ParseFrom(payload.ToArray()));
        }
        catch (InvalidProtocolBufferException)
        {
            return WebSocketEnvelopeResult.Rejected(ErrorCode.MalformedPayload, "Malformed Protobuf ClientEnvelope.");
        }
    }

    private static async Task SendErrorAndCloseAsync(WebSocket socket, ErrorCode code, string message, ulong ackSequence, string connectionId, CancellationToken cancellationToken)
    {
        await SendEnvelopeAsync(socket, CreateServerErrorEnvelope(code, message, ackSequence, connectionId), cancellationToken);

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, code.ToString(), cancellationToken);
        }
    }

    private static Task SendEnvelopeAsync(WebSocket socket, ServerEnvelope envelope, CancellationToken cancellationToken) =>
        socket.SendAsync(envelope.ToByteArray(), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

    private static ServerEnvelope CreateServerErrorEnvelope(ErrorCode code, string message, ulong ackSequence, string connectionId) =>
        new()
        {
            ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
            AckSequence = ackSequence,
            ServerError = new ServerError
            {
                Code = code,
                Message = message,
                CorrelationId = connectionId
            }
        };
}

internal sealed record WebSocketEnvelopeResult(
    ClientEnvelope? Envelope,
    ErrorCode? ErrorCode,
    string Message,
    bool CloseReceived)
{
    public static WebSocketEnvelopeResult Valid(ClientEnvelope envelope) => new(envelope, null, string.Empty, false);

    public static WebSocketEnvelopeResult Rejected(ErrorCode code, string message) => new(null, code, message, false);

    public static WebSocketEnvelopeResult Close() => new(null, null, string.Empty, true);
}

internal sealed record ClientHelloAuthResult(
    bool Success,
    StoredGameTicket? Ticket,
    ErrorCode ErrorCode,
    string Message)
{
    public static ClientHelloAuthResult Accepted(StoredGameTicket ticket) => new(true, ticket, ErrorCode.Unspecified, "ClientHello accepted.");

    public static ClientHelloAuthResult Rejected(ErrorCode code, string message) => new(false, null, code, message);
}
