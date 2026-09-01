namespace Divinity.ContractsProto.GameTickets;

public sealed class GameTicketService
{
    private readonly IGameTicketStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IGameTicketAuditSink _audit;

    public GameTicketService(IGameTicketStore store, TimeProvider? timeProvider = null, IGameTicketAuditSink? audit = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _audit = audit ?? NullGameTicketAuditSink.Instance;
    }

    public async Task<GameTicketIssueResult> IssueAsync(GameTicketIssueCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.AccountId))
        {
            _audit.Record("game_ticket_issue rejected=not_authenticated");
            return new GameTicketIssueResult(GameTicketIssueStatus.NotAuthenticated, null, null, "Authenticated account is required to issue a game ticket.");
        }

        if (string.IsNullOrWhiteSpace(command.BuildId) || string.IsNullOrWhiteSpace(command.Nonce))
        {
            _audit.Record($"game_ticket_issue rejected=invalid_request account_id={command.AccountId}");
            return new GameTicketIssueResult(GameTicketIssueStatus.InvalidRequest, null, null, "buildId and nonce are required to issue a game ticket.");
        }

        if (command.ProtocolVersion != ProtocolConstants.SupportedProtocolVersion)
        {
            _audit.Record($"game_ticket_issue rejected=unsupported_protocol account_id={command.AccountId} protocol_version={command.ProtocolVersion}");
            return new GameTicketIssueResult(GameTicketIssueStatus.UnsupportedProtocolVersion, null, null, "Unsupported protocolVersion for game ticket issue.");
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(GameTicketDefaults.TimeToLive);
        var ticket = GameTicketSecret.Create();
        var ticketHash = GameTicketSecret.Hash(ticket);
        var storedTicket = new StoredGameTicket(
            command.AccountId.Trim(),
            command.BuildId.Trim(),
            command.ProtocolVersion,
            command.Nonce.Trim(),
            now,
            expiresAt);

        await _store.StoreAsync(ticketHash, storedTicket, GameTicketDefaults.TimeToLive, cancellationToken);

        _audit.Record(
            "game_ticket_issued "
            + $"account_id={storedTicket.AccountId} "
            + $"build_id={storedTicket.BuildId} "
            + $"protocol_version={storedTicket.ProtocolVersion} "
            + $"nonce_hash={GameTicketSecret.HashNonce(storedTicket.Nonce)} "
            + $"expires_at_utc={storedTicket.ExpiresAtUtc:O}");

        return new GameTicketIssueResult(GameTicketIssueStatus.Issued, ticket, expiresAt, "Game ticket issued.");
    }

    public async Task<GameTicketConsumeResult> ConsumeAsync(GameTicketConsumeCommand command, CancellationToken cancellationToken)
    {
        if (!GameTicketSecret.IsWellFormed(command.GameTicket))
        {
            _audit.Record("game_ticket_consume rejected=malformed");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.MalformedTicket, null, "Game ticket is malformed.");
        }

        var ticketHash = GameTicketSecret.Hash(command.GameTicket);
        var storeResult = await _store.ConsumeAsync(ticketHash, _timeProvider.GetUtcNow(), cancellationToken);
        if (storeResult.Status == GameTicketStoreConsumeStatus.NotFound)
        {
            _audit.Record("game_ticket_consume rejected=invalid");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.InvalidTicket, null, "Game ticket is invalid.");
        }

        if (storeResult.Status == GameTicketStoreConsumeStatus.Reused)
        {
            _audit.Record("game_ticket_consume rejected=reused");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.ReusedTicket, storeResult.Ticket, "Game ticket was already used.");
        }

        if (storeResult.Status == GameTicketStoreConsumeStatus.Expired || storeResult.Ticket?.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _audit.Record("game_ticket_consume rejected=expired");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.ExpiredTicket, storeResult.Ticket, "Game ticket expired.");
        }

        var ticket = storeResult.Ticket;
        if (ticket is null)
        {
            _audit.Record("game_ticket_consume rejected=invalid");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.InvalidTicket, null, "Game ticket is invalid.");
        }

        if (!string.Equals(ticket.BuildId, command.BuildId, StringComparison.Ordinal))
        {
            _audit.Record($"game_ticket_consume rejected=build_mismatch account_id={ticket.AccountId}");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.BuildMismatch, ticket, "Game ticket buildId does not match ClientHello.");
        }

        if (ticket.ProtocolVersion != command.ProtocolVersion)
        {
            _audit.Record($"game_ticket_consume rejected=protocol_mismatch account_id={ticket.AccountId}");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.ProtocolMismatch, ticket, "Game ticket protocolVersion does not match ClientHello.");
        }

        if (!string.Equals(ticket.Nonce, command.Nonce, StringComparison.Ordinal))
        {
            _audit.Record($"game_ticket_consume rejected=nonce_mismatch account_id={ticket.AccountId}");
            return new GameTicketConsumeResult(GameTicketConsumeStatus.NonceMismatch, ticket, "Game ticket nonce does not match ClientHello.");
        }

        _audit.Record(
            "game_ticket_consumed "
            + $"account_id={ticket.AccountId} "
            + $"build_id={ticket.BuildId} "
            + $"protocol_version={ticket.ProtocolVersion} "
            + $"nonce_hash={GameTicketSecret.HashNonce(ticket.Nonce)}");

        return new GameTicketConsumeResult(GameTicketConsumeStatus.Consumed, ticket, "Game ticket consumed.");
    }
}
