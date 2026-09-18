using System.Text.Json;
using Divinity.GameGateway.Observability;
using Divinity.ContractsProto.GameTickets;

namespace Divinity.GameGateway.Session;

public sealed class GatewaySessionManager
{
    private const string ReconnectTokenPrefix = "rt_";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, GatewaySession> _sessionsByConnectionId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionLeaseRecord> _leasesByCharacterId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReconnectTokenRecord> _reconnectTokensByHash = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GatewayWorldSessionRecord> _worldSessionsByConnectionId = new(StringComparer.Ordinal);
    private readonly List<GatewaySessionEvent> _events = new();
    private readonly TimeProvider _timeProvider;
    private readonly string _rootDirectory;
    private readonly string _dataPath;
    private readonly string _lockPath;

    public GatewaySessionManager(TimeProvider? timeProvider = null, string? rootDirectory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? ResolveRootDirectory()
            : rootDirectory;
        _dataPath = Path.Combine(_rootDirectory, "gateway-sessions-vs018.json");
        _lockPath = Path.Combine(_rootDirectory, "gateway-sessions-vs018.lock");
        Directory.CreateDirectory(_rootDirectory);
        LoadPersistentState();
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
            _worldSessionsByConnectionId[connectionId] = new GatewayWorldSessionRecord(
                connectionId,
                ticket.AccountId,
                session.AccountPseudonym,
                null,
                "authenticated",
                now,
                now);
            _events.Add(new GatewaySessionEvent("world_session_created", connectionId, session.AccountPseudonym, null, now));
            PersistUnsafe();
        }

        return session;
    }

    public JoinLeaseResult TryJoin(string connectionId, string characterId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            PurgeExpiredLeasesUnsafe(now);
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
                PersistUnsafe();
                return new JoinLeaseResult(JoinLeaseStatus.LeaseConflict, session, existingLease, "Another connection owns the active session lease for this character.");
            }

            var lease = new SessionLeaseRecord(
                characterId,
                connectionId,
                session.AccountPseudonym,
                now.Add(GatewaySessionDefaults.LeaseTtl),
                session.AccountId);

            session.Join(characterId, now);
            _leasesByCharacterId[characterId] = lease;
            UpsertWorldSessionUnsafe(session, characterId, "joined", now);
            InvalidateReconnectTokensUnsafe(characterId, now);
            var reconnect = IssueReconnectTokenUnsafe(session, characterId, connectionId, now);
            _events.Add(new GatewaySessionEvent("join_accepted", connectionId, session.AccountPseudonym, characterId, now));
            PersistUnsafe();
            return new JoinLeaseResult(JoinLeaseStatus.Joined, session, lease, "JoinWorld accepted and session lease acquired.", reconnect);
        }
    }

    public ReconnectLeaseResult TryReconnect(string connectionId, string reconnectToken, string previousConnectionId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            PurgeExpiredLeasesUnsafe(now);
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
            {
                return new ReconnectLeaseResult(ReconnectLeaseStatus.MissingAuthenticatedSession, null, null, "Authenticated session is required before ReconnectRequest.");
            }

            if (!IsWellFormedReconnectToken(reconnectToken))
            {
                _events.Add(new GatewaySessionEvent("reconnect_rejected_invalid_token", connectionId, session.AccountPseudonym, null, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.InvalidToken, session, null, "Reconnect token is malformed.");
            }

            var tokenHash = GameTicketSecret.Hash(reconnectToken);
            if (!_reconnectTokensByHash.TryGetValue(tokenHash, out var token)
                || token.ConsumedAtUtc is not null)
            {
                _events.Add(new GatewaySessionEvent("reconnect_rejected_invalid_token", connectionId, session.AccountPseudonym, null, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.InvalidToken, session, null, "Reconnect token is invalid or already consumed.");
            }

            if (token.ExpiresAtUtc <= now)
            {
                _reconnectTokensByHash[tokenHash] = token with { ConsumedAtUtc = now };
                _events.Add(new GatewaySessionEvent("reconnect_rejected_token_expired", connectionId, session.AccountPseudonym, token.CharacterId, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.LeaseExpired, session, null, "Reconnect token is expired.");
            }

            if (!string.Equals(token.AccountId, session.AccountId, StringComparison.Ordinal))
            {
                _events.Add(new GatewaySessionEvent("reconnect_rejected_account_mismatch", connectionId, session.AccountPseudonym, token.CharacterId, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.AccountMismatch, session, null, "Reconnect token does not belong to the authenticated account.");
            }

            if (!string.IsNullOrWhiteSpace(previousConnectionId)
                && !string.Equals(previousConnectionId, token.ConnectionId, StringComparison.Ordinal)
                && !string.Equals(previousConnectionId, token.PreviousConnectionId, StringComparison.Ordinal))
            {
                _events.Add(new GatewaySessionEvent("reconnect_rejected_previous_connection", connectionId, session.AccountPseudonym, token.CharacterId, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.InvalidToken, session, null, "ReconnectRequest previous_connection_id does not match the token.");
            }

            if (!_leasesByCharacterId.TryGetValue(token.CharacterId, out var lease) || lease.ExpiresAtUtc <= now)
            {
                _reconnectTokensByHash[tokenHash] = token with { ConsumedAtUtc = now };
                _events.Add(new GatewaySessionEvent("reconnect_rejected_lease_expired", connectionId, session.AccountPseudonym, token.CharacterId, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.LeaseExpired, session, lease, "Reconnect lease is expired.");
            }

            if (!string.IsNullOrWhiteSpace(lease.AccountId)
                && !string.Equals(lease.AccountId, session.AccountId, StringComparison.Ordinal))
            {
                _events.Add(new GatewaySessionEvent("reconnect_rejected_lease_account_mismatch", connectionId, session.AccountPseudonym, token.CharacterId, now));
                PersistUnsafe();
                return new ReconnectLeaseResult(ReconnectLeaseStatus.AccountMismatch, session, lease, "Reconnect lease belongs to a different account.");
            }

            _sessionsByConnectionId.Remove(token.ConnectionId);
            _reconnectTokensByHash[tokenHash] = token with { ConsumedAtUtc = now };
            session.Join(token.CharacterId, now);
            var renewedLease = lease with
            {
                ConnectionId = connectionId,
                AccountPseudonym = session.AccountPseudonym,
                AccountId = session.AccountId,
                ExpiresAtUtc = now.Add(GatewaySessionDefaults.ReconnectGrace),
                DisconnectedAtUtc = null,
                CombatGraceExpiresAtUtc = null
            };
            _leasesByCharacterId[token.CharacterId] = renewedLease;
            UpsertWorldSessionUnsafe(session, token.CharacterId, "reconnected", now);
            var nextToken = IssueReconnectTokenUnsafe(session, token.CharacterId, connectionId, now);
            _events.Add(new GatewaySessionEvent("reconnect_accepted", connectionId, session.AccountPseudonym, token.CharacterId, now));
            PersistUnsafe();
            return new ReconnectLeaseResult(ReconnectLeaseStatus.Reconnected, session, renewedLease, "Reconnect accepted and actor reassociated.", nextToken);
        }
    }

    public HeartbeatLeaseResult RenewHeartbeat(string connectionId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            PurgeExpiredLeasesUnsafe(now);
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
                PersistUnsafe();
                return new HeartbeatLeaseResult(HeartbeatLeaseStatus.LeaseConflict, session, currentLease, "Connection does not own the active lease.");
            }

            var renewedLease = currentLease with
            {
                ExpiresAtUtc = now.Add(GatewaySessionDefaults.LeaseTtl),
                DisconnectedAtUtc = null,
                CombatGraceExpiresAtUtc = null
            };
            _leasesByCharacterId[session.CharacterId] = renewedLease;
            session.MarkSeen(now);
            UpsertWorldSessionUnsafe(session, session.CharacterId, "heartbeat", now);
            _events.Add(new GatewaySessionEvent("heartbeat_renewed", connectionId, session.AccountPseudonym, session.CharacterId, now));
            PersistUnsafe();
            return new HeartbeatLeaseResult(HeartbeatLeaseStatus.Renewed, session, renewedLease, "Heartbeat renewed the session lease.");
        }
    }

    public MoveIntentRateLimitResult TryAcquireMoveIntent(string connectionId)
    {
        var result = TryAcquireMessageIntent(connectionId, GatewayRateLimitCategory.Move);
        return new MoveIntentRateLimitResult(
            result.Status == GatewayMessageRateLimitStatus.Accepted
                ? MoveIntentRateLimitStatus.Accepted
                : result.Status == GatewayMessageRateLimitStatus.MissingAuthenticatedSession
                    ? MoveIntentRateLimitStatus.MissingAuthenticatedSession
                    : MoveIntentRateLimitStatus.RateLimited,
            result.Session,
            result.Message);
    }

    public GatewayMessageRateLimitResult TryAcquireMessageIntent(string connectionId, GatewayRateLimitCategory category)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
            {
                return new GatewayMessageRateLimitResult(
                    GatewayMessageRateLimitStatus.MissingAuthenticatedSession,
                    null,
                    category,
                    $"Authenticated session is required before {category} intent.");
            }

            if (!session.TryAcquire(category, now))
            {
                _events.Add(new GatewaySessionEvent($"{category.ToString().ToLowerInvariant()}_rate_limited", connectionId, session.AccountPseudonym, session.CharacterId, now));
                GatewayTelemetry.RecordRateLimit(category);
                return new GatewayMessageRateLimitResult(
                    GatewayMessageRateLimitStatus.RateLimited,
                    session,
                    category,
                    $"{category} rate limit exceeded.");
            }

            return new GatewayMessageRateLimitResult(
                GatewayMessageRateLimitStatus.Accepted,
                session,
                category,
                $"{category} accepted by gateway rate limit.");
        }
    }

    public GatewayDisconnectResult Disconnect(
        string connectionId,
        string reason,
        bool preserveLeaseForReconnect = false,
        DateTimeOffset? combatGraceExpiresAtUtc = null)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_sessionsByConnectionId.Remove(connectionId, out var session))
            {
                return new GatewayDisconnectResult(null, LeasePreserved: false, LeaseExpiresAtUtc: null);
            }

            DateTimeOffset? leaseExpiresAt = null;
            var leasePreserved = false;
            if (!string.IsNullOrWhiteSpace(session.CharacterId)
                && _leasesByCharacterId.TryGetValue(session.CharacterId, out var lease)
                && string.Equals(lease.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                if (preserveLeaseForReconnect)
                {
                    var renewedLease = lease with
                    {
                        ExpiresAtUtc = now.Add(GatewaySessionDefaults.ReconnectGrace),
                        DisconnectedAtUtc = now,
                        CombatGraceExpiresAtUtc = combatGraceExpiresAtUtc
                    };
                    _leasesByCharacterId[session.CharacterId] = renewedLease;
                    leaseExpiresAt = renewedLease.ExpiresAtUtc;
                    leasePreserved = true;
                }
                else
                {
                    _leasesByCharacterId.Remove(session.CharacterId);
                    InvalidateReconnectTokensUnsafe(session.CharacterId, now);
                }
            }

            UpsertWorldSessionUnsafe(session, session.CharacterId, preserveLeaseForReconnect ? "disconnected_grace" : "disconnected", now);
            _events.Add(new GatewaySessionEvent(reason, connectionId, session.AccountPseudonym, session.CharacterId, now));
            PersistUnsafe();
            return new GatewayDisconnectResult(session.CharacterId, leasePreserved, leaseExpiresAt);
        }
    }

    public IReadOnlyList<SessionLeaseRecord> ExpireReconnectLeases()
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            var expired = PurgeExpiredLeasesUnsafe(now);
            PersistUnsafe();
            return expired;
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

    public GatewaySessionStoreDocument ReadDocumentForTesting()
    {
        lock (_gate)
        {
            return CreateDocumentUnsafe();
        }
    }

    private ReconnectTokenIssue IssueReconnectTokenUnsafe(
        GatewaySession session,
        string characterId,
        string previousConnectionId,
        DateTimeOffset now)
    {
        var token = ReconnectTokenPrefix + Base64Url.CreateRandom(32);
        var issue = new ReconnectTokenIssue(token, now.Add(GatewaySessionDefaults.ReconnectGrace));
        var record = new ReconnectTokenRecord(
            GameTicketSecret.Hash(token),
            session.AccountId,
            characterId,
            session.ConnectionId,
            previousConnectionId,
            now,
            issue.ExpiresAtUtc,
            ConsumedAtUtc: null);
        _reconnectTokensByHash[record.TokenHash] = record;
        return issue;
    }

    private void InvalidateReconnectTokensUnsafe(string characterId, DateTimeOffset now)
    {
        foreach (var token in _reconnectTokensByHash.Values.Where(token =>
            token.CharacterId == characterId &&
            token.ConsumedAtUtc is null).ToArray())
        {
            _reconnectTokensByHash[token.TokenHash] = token with { ConsumedAtUtc = now };
        }
    }

    private IReadOnlyList<SessionLeaseRecord> PurgeExpiredLeasesUnsafe(DateTimeOffset now)
    {
        var expired = _leasesByCharacterId.Values
            .Where(lease => lease.ExpiresAtUtc <= now)
            .ToArray();
        foreach (var lease in expired)
        {
            _leasesByCharacterId.Remove(lease.CharacterId);
            InvalidateReconnectTokensUnsafe(lease.CharacterId, now);
            _events.Add(new GatewaySessionEvent("session_lease_expired", lease.ConnectionId, lease.AccountPseudonym, lease.CharacterId, now));
        }

        return expired;
    }

    private void UpsertWorldSessionUnsafe(GatewaySession session, string? characterId, string status, DateTimeOffset now)
    {
        _worldSessionsByConnectionId[session.ConnectionId] = new GatewayWorldSessionRecord(
            session.ConnectionId,
            session.AccountId,
            session.AccountPseudonym,
            characterId,
            status,
            session.CreatedAtUtc,
            now);
    }

    private void LoadPersistentState()
    {
        lock (_gate)
        {
            var document = ReadDocumentFromDisk();
            foreach (var lease in document.SessionLeases)
            {
                _leasesByCharacterId[lease.CharacterId] = lease;
            }

            foreach (var token in document.ReconnectTokens)
            {
                _reconnectTokensByHash[token.TokenHash] = token;
            }

            foreach (var worldSession in document.WorldSessions)
            {
                _worldSessionsByConnectionId[worldSession.ConnectionId] = worldSession;
            }
        }
    }

    private void PersistUnsafe()
    {
        Directory.CreateDirectory(_rootDirectory);
        using var fileLock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var tempPath = _dataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(CreateDocumentUnsafe(), JsonOptions) + Environment.NewLine);
        File.Move(tempPath, _dataPath, overwrite: true);
    }

    private GatewaySessionStoreDocument ReadDocumentFromDisk()
    {
        Directory.CreateDirectory(_rootDirectory);
        using var fileLock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!File.Exists(_dataPath))
        {
            return new GatewaySessionStoreDocument([], [], []);
        }

        var json = File.ReadAllText(_dataPath);
        var document = JsonSerializer.Deserialize<GatewaySessionStoreDocument>(json, JsonOptions)
            ?? new GatewaySessionStoreDocument([], [], []);
        return new GatewaySessionStoreDocument(
            document.WorldSessions ?? [],
            document.SessionLeases ?? [],
            document.ReconnectTokens ?? []);
    }

    private GatewaySessionStoreDocument CreateDocumentUnsafe() =>
        new(
            _worldSessionsByConnectionId.Values.OrderBy(session => session.CreatedAtUtc).ToList(),
            _leasesByCharacterId.Values.OrderBy(lease => lease.CharacterId).ToList(),
            _reconnectTokensByHash.Values.OrderBy(token => token.IssuedAtUtc).ToList());

    private static bool IsWellFormedReconnectToken(string token) =>
        token.StartsWith(ReconnectTokenPrefix, StringComparison.Ordinal)
        && token.Length >= 32
        && token.Skip(ReconnectTokenPrefix.Length).All(character =>
            character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_');

    private static string ResolveRootDirectory()
    {
        var configuredPath = Environment.GetEnvironmentVariable("DIVINITY_GATEWAY_SESSION_STORE_PATH");
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Path.GetTempPath(), "divinity", "gateway-sessions")
            : configuredPath;
    }
}
