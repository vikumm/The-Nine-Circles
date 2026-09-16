using Divinity.ContractsProto.GameTickets;

namespace Divinity.GameGateway.Session;

public sealed class GatewaySessionManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GatewaySession> _sessionsByConnectionId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionLeaseRecord> _leasesByCharacterId = new(StringComparer.Ordinal);
    private readonly List<GatewaySessionEvent> _events = new();
    private readonly TimeProvider _timeProvider;

    public GatewaySessionManager(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public GatewaySession CreateAuthenticatedSession(string connectionId, StoredGameTicket ticket)
    {
        var now = _timeProvider.GetUtcNow();
        var session = new GatewaySession(
            connectionId,
            ticket.AccountId,
            AccountPseudonym.Create(ticket.AccountId),
            ticket.BuildId,
            ticket.ProtocolVersion,
            ticket.Nonce,
            now);

        lock (_gate)
        {
            _sessionsByConnectionId[connectionId] = session;
            _events.Add(new GatewaySessionEvent("world_session_created", connectionId, session.AccountPseudonym, null, now));
        }

        return session;
    }

    public JoinLeaseResult TryJoin(string connectionId, string characterId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
            {
                return new JoinLeaseResult(JoinLeaseStatus.MissingAuthenticatedSession, null, null, "Authenticated session is required before JoinWorld.");
            }

            if (string.IsNullOrWhiteSpace(characterId))
            {
                return new JoinLeaseResult(JoinLeaseStatus.MissingCharacterId, session, null, "JoinWorld requires character_id.");
            }

            if (_leasesByCharacterId.TryGetValue(characterId, out var existingLease)
                && existingLease.ExpiresAtUtc > now
                && !string.Equals(existingLease.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                _events.Add(new GatewaySessionEvent("join_rejected_lease_conflict", connectionId, session.AccountPseudonym, characterId, now));
                return new JoinLeaseResult(JoinLeaseStatus.LeaseConflict, session, existingLease, "Another connection owns the active session lease for this character.");
            }

            var lease = new SessionLeaseRecord(
                characterId,
                connectionId,
                session.AccountPseudonym,
                now.Add(GatewaySessionDefaults.LeaseTtl));

            session.Join(characterId, now);
            _leasesByCharacterId[characterId] = lease;
            _events.Add(new GatewaySessionEvent("join_accepted", connectionId, session.AccountPseudonym, characterId, now));
            return new JoinLeaseResult(JoinLeaseStatus.Joined, session, lease, "JoinWorld accepted and session lease acquired.");
        }
    }

    public HeartbeatLeaseResult RenewHeartbeat(string connectionId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
            {
                return new HeartbeatLeaseResult(HeartbeatLeaseStatus.MissingAuthenticatedSession, null, null, "Authenticated session is required before Heartbeat.");
            }

            if (string.IsNullOrWhiteSpace(session.CharacterId))
            {
                return new HeartbeatLeaseResult(HeartbeatLeaseStatus.SessionNotJoined, session, null, "JoinWorld is required before Heartbeat can renew a lease.");
            }

            if (!_leasesByCharacterId.TryGetValue(session.CharacterId, out var currentLease)
                || !string.Equals(currentLease.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                _events.Add(new GatewaySessionEvent("heartbeat_rejected_lease_conflict", connectionId, session.AccountPseudonym, session.CharacterId, now));
                return new HeartbeatLeaseResult(HeartbeatLeaseStatus.LeaseConflict, session, currentLease, "Connection does not own the active lease.");
            }

            var renewedLease = currentLease with { ExpiresAtUtc = now.Add(GatewaySessionDefaults.LeaseTtl) };
            _leasesByCharacterId[session.CharacterId] = renewedLease;
            session.MarkSeen(now);
            _events.Add(new GatewaySessionEvent("heartbeat_renewed", connectionId, session.AccountPseudonym, session.CharacterId, now));
            return new HeartbeatLeaseResult(HeartbeatLeaseStatus.Renewed, session, renewedLease, "Heartbeat renewed the session lease.");
        }
    }

    public MoveIntentRateLimitResult TryAcquireMoveIntent(string connectionId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
            {
                return new MoveIntentRateLimitResult(MoveIntentRateLimitStatus.MissingAuthenticatedSession, null, "Authenticated session is required before MoveIntent.");
            }

            if (!session.TryAcquireMoveIntent(now))
            {
                _events.Add(new GatewaySessionEvent("move_intent_rate_limited", connectionId, session.AccountPseudonym, session.CharacterId, now));
                return new MoveIntentRateLimitResult(MoveIntentRateLimitStatus.RateLimited, session, "MoveIntent rate limit exceeded.");
            }

            return new MoveIntentRateLimitResult(MoveIntentRateLimitStatus.Accepted, session, "MoveIntent accepted by gateway rate limit.");
        }
    }

    public void Disconnect(string connectionId, string reason)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_sessionsByConnectionId.Remove(connectionId, out var session))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.CharacterId)
                && _leasesByCharacterId.TryGetValue(session.CharacterId, out var lease)
                && string.Equals(lease.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                _leasesByCharacterId.Remove(session.CharacterId);
            }

            _events.Add(new GatewaySessionEvent(reason, connectionId, session.AccountPseudonym, session.CharacterId, now));
        }
    }

    public GatewaySession? GetSession(string connectionId)
    {
        lock (_gate)
        {
            return _sessionsByConnectionId.TryGetValue(connectionId, out var session) ? session : null;
        }
    }

    public SessionLeaseRecord? GetLease(string characterId)
    {
        lock (_gate)
        {
            return _leasesByCharacterId.TryGetValue(characterId, out var lease) ? lease : null;
        }
    }

    public IReadOnlyList<GatewaySessionEvent> SnapshotEvents()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }
}
