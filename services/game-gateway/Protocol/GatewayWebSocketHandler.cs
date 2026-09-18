using System.Net.WebSockets;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway.Observability;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Inventory;
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
    private readonly InventoryEquipmentService _inventoryService;
    private readonly GatewaySessionManager _sessionManager;
    private readonly ILogger<GatewayWebSocketHandler> _logger;

    public GatewayWebSocketHandler(
        GameTicketService ticketService,
        CharacterService characterService,
        WorldMovementRuntime worldRuntime,
        WorldMonsterRuntime monsterRuntime,
        WorldCombatRuntime combatRuntime,
        InventoryEquipmentService inventoryService,
        GatewaySessionManager sessionManager,
        ILogger<GatewayWebSocketHandler> logger)
    {
        _ticketService = ticketService;
        _characterService = characterService;
        _worldRuntime = worldRuntime;
        _monsterRuntime = monsterRuntime;
        _combatRuntime = combatRuntime;
        _inventoryService = inventoryService;
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
                GatewayTelemetry.RecordAuthentication(false);
                await SendErrorAndCloseAsync(socket, authResult.ErrorCode, authResult.Message, helloEnvelope.Sequence, connectionId, cancellationToken);
                disconnectReason = "disconnect_handshake_rejected";
                _logger.LogInformation(
                    "gateway_ws event=client_hello connection_id={ConnectionId} account_pseudonym=anonymous result=rejected error_code={ErrorCode}",
                    connectionId,
                    authResult.ErrorCode);
                return;
            }

            session = _sessionManager.CreateAuthenticatedSession(connectionId, authResult.Ticket!);
            GatewayTelemetry.RecordAuthentication(true);
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
        catch (WebSocketException)
        {
            disconnectReason = "disconnect_abrupt";
        }
        finally
        {
            if (session is not null)
            {
                WorldCharacterDisconnectResult? worldDisconnect = null;
                if (!string.IsNullOrWhiteSpace(session.CharacterId))
                {
                    worldDisconnect = await _worldRuntime.DisconnectAsync(session.CharacterId, disconnectReason, CancellationToken.None);
                    _monsterRuntime.DisconnectPlayer(session.CharacterId);
                }

                _sessionManager.Disconnect(
                    connectionId,
                    disconnectReason,
                    preserveLeaseForReconnect: IsReconnectGraceDisconnect(disconnectReason),
                    worldDisconnect?.CombatGraceExpiresAtUtc);
                GatewayTelemetry.RecordDisconnect(disconnectReason);
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
            GatewayTelemetry.RecordMessage(envelope.PayloadCase.ToString(), "received");
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
                case ClientEnvelope.PayloadOneofCase.CastIntent:
                    await HandleCastIntentAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.EquipItemIntent:
                    await HandleEquipItemIntentAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.UnequipItemIntent:
                    await HandleUnequipItemIntentAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.Heartbeat:
                    await HandleHeartbeatAsync(socket, connectionId, session, envelope.Sequence, cancellationToken);
                    break;
                case ClientEnvelope.PayloadOneofCase.ReconnectRequest:
                    await HandleReconnectRequestAsync(socket, connectionId, session, envelope, cancellationToken);
                    break;
                default:
                    await SendEnvelopeAsync(
                        socket,
                        CreateServerErrorEnvelope(ErrorCode.UnknownPayloadType, "Authenticated WSS accepts JoinWorld, MoveIntent, AttackIntent, CastIntent, EquipItemIntent, UnequipItemIntent, Heartbeat and ReconnectRequest stub.", envelope.Sequence, connectionId),
                        cancellationToken);
                    break;
            }
        }
    }

    private async Task HandleJoinWorldAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.auth_ticket_join", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "join_world");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Join, ErrorCode.SessionLeaseConflict, envelope.Sequence, cancellationToken))
        {
            return;
        }

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
            await SendJoinStateAsync(socket, envelope.Sequence, worldJoin, result.ReconnectToken, character, includeInventory: false, cancellationToken);

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
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.move_intent", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "move_intent");
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
            GatewayTelemetry.RecordMessage("MoveIntent", "rate_limited");
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

    private async Task HandleReconnectRequestAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.disconnect_checkpoint_reconnect", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "reconnect_request");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Reconnect, ErrorCode.ReconnectRejected, envelope.Sequence, cancellationToken))
        {
            return;
        }

        var reconnect = envelope.ReconnectRequest;
        var leaseResult = _sessionManager.TryReconnect(connectionId, reconnect.ReconnectToken, reconnect.PreviousConnectionId);
        if (leaseResult.Status != ReconnectLeaseStatus.Reconnected || leaseResult.Lease is null)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.ReconnectRejected, leaseResult.Message, envelope.Sequence, connectionId),
                cancellationToken);
            _logger.LogInformation(
                "gateway_ws event=reconnect_request connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
                connectionId,
                session.AccountPseudonym,
                leaseResult.Status,
                ErrorCode.ReconnectRejected);
            return;
        }

        var characterResult = await _characterService.GetOwnedCharacterAsync(session.AccountId, leaseResult.Lease.CharacterId, cancellationToken);
        if (characterResult.Status != CharacterServiceStatus.Selected)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.CharacterNotOwned, characterResult.Message, envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var character = characterResult.Character!;
        var worldJoin = await _worldRuntime.JoinAsync(character, cancellationToken);
        _monsterRuntime.UpsertPlayer(
            worldJoin.CharacterId,
            (double)worldJoin.Position.X,
            (double)worldJoin.Position.Y,
            hp: worldJoin.Stats.Hp);

        await SendJoinStateAsync(socket, envelope.Sequence, worldJoin, leaseResult.ReconnectToken, character, includeInventory: true, cancellationToken);
        _logger.LogInformation(
            "gateway_ws event=reconnect_request connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result=accepted error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            ErrorCode.Unspecified);
    }

    private async Task HandleAttackIntentAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.attack_intent", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "attack_intent");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Combat, ErrorCode.AttackRejected, envelope.Sequence, cancellationToken))
        {
            return;
        }

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

    private async Task HandleCastIntentAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.cast_intent", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "cast_intent");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Combat, ErrorCode.CastRejected, envelope.Sequence, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.CharacterId))
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.SessionNotJoined, "JoinWorld is required before CastIntent.", envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var result = _combatRuntime.ApplyShieldBash(session.CharacterId, envelope.Sequence, envelope.CastIntent);
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

        if (result.SkillStateChanged is not null)
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

        if (result.CharacterProgressed is not null)
        {
            await SendEnvelopeAsync(
                socket,
                new ServerEnvelope
                {
                    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                    AckSequence = envelope.Sequence,
                    CharacterProgressed = result.CharacterProgressed
                },
                cancellationToken);
        }

        if (result.CombatEvent is null && result.SkillStateChanged is null && result.CharacterProgressed is null)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(result.ErrorCode, result.Message, envelope.Sequence, connectionId),
                cancellationToken);
        }

        _logger.LogInformation(
            "gateway_ws event=cast_intent connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            result.Status,
            result.ErrorCode);
    }

    private async Task HandleEquipItemIntentAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.equip_validation_persistence", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "equip_item_intent");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Inventory, ErrorCode.InventoryRejected, envelope.Sequence, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.CharacterId))
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.SessionNotJoined, "JoinWorld is required before EquipItemIntent.", envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var characterResult = await _characterService.GetOwnedCharacterAsync(session.AccountId, session.CharacterId, cancellationToken);
        if (characterResult.Status != CharacterServiceStatus.Selected)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.CharacterNotOwned, characterResult.Message, envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var intent = envelope.EquipItemIntent;
        var result = _inventoryService.EquipItem(new InventoryEquipItemCommand(
            InventoryActor.FromCharacter(characterResult.Character!),
            intent.ItemInstanceId,
            ToInventorySlot(intent.Slot),
            intent.InventoryVersion));

        await SendInventoryResultAsync(socket, connectionId, envelope.Sequence, result, cancellationToken);
        _logger.LogInformation(
            "gateway_ws event=equip_item_intent connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            result.Status,
            result.Accepted ? ErrorCode.Unspecified : ErrorCode.InventoryRejected);
    }

    private async Task HandleUnequipItemIntentAsync(WebSocket socket, string connectionId, GatewaySession session, ClientEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.equip_validation_persistence", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "unequip_item_intent");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Inventory, ErrorCode.InventoryRejected, envelope.Sequence, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.CharacterId))
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.SessionNotJoined, "JoinWorld is required before UnequipItemIntent.", envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var characterResult = await _characterService.GetOwnedCharacterAsync(session.AccountId, session.CharacterId, cancellationToken);
        if (characterResult.Status != CharacterServiceStatus.Selected)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.CharacterNotOwned, characterResult.Message, envelope.Sequence, connectionId),
                cancellationToken);
            return;
        }

        var intent = envelope.UnequipItemIntent;
        var result = _inventoryService.UnequipItem(new InventoryUnequipItemCommand(
            InventoryActor.FromCharacter(characterResult.Character!),
            ToInventorySlot(intent.Slot),
            intent.InventoryVersion));

        await SendInventoryResultAsync(socket, connectionId, envelope.Sequence, result, cancellationToken);
        _logger.LogInformation(
            "gateway_ws event=unequip_item_intent connection_id={ConnectionId} account_pseudonym={AccountPseudonym} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            result.Status,
            result.Accepted ? ErrorCode.Unspecified : ErrorCode.InventoryRejected);
    }

    private async Task HandleHeartbeatAsync(WebSocket socket, string connectionId, GatewaySession session, ulong sequence, CancellationToken cancellationToken)
    {
        using var activity = GatewayTelemetry.StartActivity("divinity.gateway.heartbeat", connectionId, session.AccountPseudonym, session.CharacterId, GatewaySessionDefaults.StubMapId, "heartbeat");
        if (!await EnsureRateLimitAsync(socket, connectionId, session, GatewayRateLimitCategory.Heartbeat, ErrorCode.HeartbeatRejected, sequence, cancellationToken))
        {
            return;
        }

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

    private async Task<bool> EnsureRateLimitAsync(
        WebSocket socket,
        string connectionId,
        GatewaySession session,
        GatewayRateLimitCategory category,
        ErrorCode errorCode,
        ulong sequence,
        CancellationToken cancellationToken)
    {
        var result = _sessionManager.TryAcquireMessageIntent(connectionId, category);
        if (result.Status == GatewayMessageRateLimitStatus.Accepted)
        {
            return true;
        }

        GatewayTelemetry.RecordMessage(category.ToString(), "rate_limited");
        await SendEnvelopeAsync(
            socket,
            CreateServerErrorEnvelope(errorCode, result.Message, sequence, connectionId),
            cancellationToken);
        _logger.LogInformation(
            "gateway_ws event=rate_limit connection_id={ConnectionId} account_pseudonym={AccountPseudonym} category={Category} result={Result} error_code={ErrorCode}",
            connectionId,
            session.AccountPseudonym,
            category,
            result.Status,
            errorCode);
        return false;
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

    private async Task SendJoinStateAsync(
        WebSocket socket,
        ulong sequence,
        WorldJoinResult worldJoin,
        ReconnectTokenIssue? reconnectToken,
        CharacterRecord character,
        bool includeInventory,
        CancellationToken cancellationToken)
    {
        await SendEnvelopeAsync(
            socket,
            new ServerEnvelope
            {
                ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                AckSequence = sequence,
                JoinAccepted = new JoinAccepted
                {
                    CharacterId = worldJoin.CharacterId,
                    MapId = worldJoin.MapId,
                    ChannelId = worldJoin.ChannelId,
                    Position = ToVector2(worldJoin.Position),
                    Stats = worldJoin.Stats,
                    ContentHash = worldJoin.ContentHash,
                    ReconnectToken = reconnectToken?.Token ?? string.Empty,
                    ReconnectTtlSeconds = reconnectToken is null
                        ? 0
                        : (uint)Math.Max(0, Math.Ceiling((reconnectToken.ExpiresAtUtc - TimeProvider.System.GetUtcNow()).TotalSeconds))
                }
            },
            cancellationToken);
        await SendEnvelopeAsync(socket, CreateSnapshotEnvelope(worldJoin.Snapshot, sequence), cancellationToken);
        if (!includeInventory)
        {
            return;
        }

        var inventory = _inventoryService.GetOrCreateInventory(InventoryActor.FromCharacter(character));
        await SendEnvelopeAsync(
            socket,
            new ServerEnvelope
            {
                ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                AckSequence = sequence,
                InventoryDelta = ToInventoryDelta(inventory)
            },
            cancellationToken);
    }

    private static InventoryEquipmentSlot ToInventorySlot(EquipmentSlot slot) =>
        slot == EquipmentSlot.OffHand ? InventoryEquipmentSlot.OffHand : InventoryEquipmentSlot.Unspecified;

    private static EquipmentSlot ToContractSlot(InventoryEquipmentSlot slot) =>
        slot == InventoryEquipmentSlot.OffHand ? EquipmentSlot.OffHand : EquipmentSlot.Unspecified;

    private static bool IsReconnectGraceDisconnect(string reason) =>
        reason.Contains("abrupt", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("transport", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private static async Task SendInventoryResultAsync(
        WebSocket socket,
        string connectionId,
        ulong sequence,
        InventoryOperationResult result,
        CancellationToken cancellationToken)
    {
        if (!result.Accepted)
        {
            await SendEnvelopeAsync(
                socket,
                CreateServerErrorEnvelope(ErrorCode.InventoryRejected, result.Message, sequence, connectionId),
                cancellationToken);
            return;
        }

        await SendEnvelopeAsync(
            socket,
            new ServerEnvelope
            {
                ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
                AckSequence = sequence,
                InventoryDelta = ToInventoryDelta(result.Snapshot)
            },
            cancellationToken);
    }

    private static InventoryDelta ToInventoryDelta(InventorySnapshot snapshot)
    {
        var delta = new InventoryDelta
        {
            InventoryVersion = snapshot.InventoryVersion,
            CurrencyBalance = (uint)Math.Max(0, snapshot.CurrencyBalance)
        };

        foreach (var slot in snapshot.Slots.Where(slot => !string.IsNullOrWhiteSpace(slot.ItemInstanceId)))
        {
            delta.Slots.Add(new InventorySlot
            {
                SlotIndex = slot.SlotIndex,
                ItemInstanceId = slot.ItemInstanceId
            });
        }

        foreach (var equipment in snapshot.Equipment)
        {
            delta.Equipment.Add(new EquipmentItem
            {
                Slot = ToContractSlot(equipment.Slot),
                ItemInstanceId = equipment.ItemInstanceId,
                Durability = (uint)Math.Max(0, equipment.Durability),
                MaxDurability = (uint)Math.Max(0, equipment.MaxDurability),
                AttributesActive = equipment.AttributesActive
            });
        }

        return delta;
    }

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
