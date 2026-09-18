namespace Divinity.GameGateway.Session;

public static class GatewaySessionDefaults
{
    public const int AnonymousHandshakeLimit = 3;
    public const int MoveIntentLimit = 20;
    public const int CombatIntentLimit = 12;
    public const int InventoryIntentLimit = 6;
    public const int HeartbeatIntentLimit = 4;
    public const int JoinIntentLimit = 3;
    public const int ReconnectIntentLimit = 2;
    public const string StubMapId = "training-field-01";
    public const string StubChannelId = "gateway-local";
    public const string StubContentHash = "6529839f9a9e7a0f1dc939a9e72fa7b6938f588f24317b5df64fc1507136a89e";
    public static readonly TimeSpan AnonymousHandshakeWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MoveIntentWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan CombatIntentWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan InventoryIntentWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan HeartbeatIntentWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan JoinIntentWindow = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReconnectIntentWindow = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CombatDisconnectGrace = TimeSpan.FromSeconds(10);

    public static GatewayRateLimitRule GetRateLimitRule(GatewayRateLimitCategory category) =>
        category switch
        {
            GatewayRateLimitCategory.Move => new GatewayRateLimitRule(MoveIntentLimit, MoveIntentWindow),
            GatewayRateLimitCategory.Combat => new GatewayRateLimitRule(CombatIntentLimit, CombatIntentWindow),
            GatewayRateLimitCategory.Inventory => new GatewayRateLimitRule(InventoryIntentLimit, InventoryIntentWindow),
            GatewayRateLimitCategory.Heartbeat => new GatewayRateLimitRule(HeartbeatIntentLimit, HeartbeatIntentWindow),
            GatewayRateLimitCategory.Join => new GatewayRateLimitRule(JoinIntentLimit, JoinIntentWindow),
            GatewayRateLimitCategory.Reconnect => new GatewayRateLimitRule(ReconnectIntentLimit, ReconnectIntentWindow),
            _ => new GatewayRateLimitRule(MoveIntentLimit, MoveIntentWindow)
        };
}

public sealed class GatewaySession
{
    private readonly Dictionary<GatewayRateLimitCategory, Queue<DateTimeOffset>> _attemptsByCategory = new();

    public GatewaySession(
        string connectionId,
        string accountId,
        string accountPseudonym,
        string buildId,
        uint protocolVersion,
        string clientNonce,
        DateTimeOffset createdAtUtc)
    {
        ConnectionId = connectionId;
        AccountId = accountId;
        AccountPseudonym = accountPseudonym;
        BuildId = buildId;
        ProtocolVersion = protocolVersion;
        ClientNonce = clientNonce;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = createdAtUtc;
    }

    public string ConnectionId { get; }
    public string AccountId { get; }
    public string AccountPseudonym { get; }
    public string BuildId { get; }
    public uint ProtocolVersion { get; }
    public string ClientNonce { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public string? CharacterId { get; private set; }

    public void Join(string characterId, DateTimeOffset nowUtc)
    {
        CharacterId = characterId;
        LastSeenAtUtc = nowUtc;
    }

    public void MarkSeen(DateTimeOffset nowUtc) => LastSeenAtUtc = nowUtc;

    public bool TryAcquireMoveIntent(DateTimeOffset nowUtc) =>
        TryAcquire(GatewayRateLimitCategory.Move, nowUtc);

    public bool TryAcquire(GatewayRateLimitCategory category, DateTimeOffset nowUtc)
    {
        var rule = GatewaySessionDefaults.GetRateLimitRule(category);
        if (!_attemptsByCategory.TryGetValue(category, out var attempts))
        {
            attempts = new Queue<DateTimeOffset>();
            _attemptsByCategory[category] = attempts;
        }

        while (attempts.Count > 0
            && nowUtc - attempts.Peek() >= rule.Window)
        {
            attempts.Dequeue();
        }

        if (attempts.Count >= rule.Limit)
        {
            return false;
        }

        attempts.Enqueue(nowUtc);
        LastSeenAtUtc = nowUtc;
        return true;
    }
}

public enum GatewayRateLimitCategory
{
    Move,
    Combat,
    Inventory,
    Heartbeat,
    Join,
    Reconnect
}

public sealed record GatewayRateLimitRule(int Limit, TimeSpan Window);

public sealed record SessionLeaseRecord(
    string CharacterId,
    string ConnectionId,
    string AccountPseudonym,
    DateTimeOffset ExpiresAtUtc,
    string AccountId = "",
    DateTimeOffset? DisconnectedAtUtc = null,
    DateTimeOffset? CombatGraceExpiresAtUtc = null);

public sealed record GatewaySessionEvent(
    string Kind,
    string ConnectionId,
    string AccountPseudonym,
    string? CharacterId,
    DateTimeOffset RecordedAtUtc);

public sealed record JoinLeaseResult(
    JoinLeaseStatus Status,
    GatewaySession? Session,
    SessionLeaseRecord? Lease,
    string Message,
    ReconnectTokenIssue? ReconnectToken = null);

public enum JoinLeaseStatus
{
    Joined,
    MissingAuthenticatedSession,
    MissingCharacterId,
    CharacterNotOwned,
    LeaseConflict
}

public sealed record HeartbeatLeaseResult(
    HeartbeatLeaseStatus Status,
    GatewaySession? Session,
    SessionLeaseRecord? Lease,
    string Message);

public enum HeartbeatLeaseStatus
{
    Renewed,
    MissingAuthenticatedSession,
    SessionNotJoined,
    LeaseConflict
}

public sealed record MoveIntentRateLimitResult(
    MoveIntentRateLimitStatus Status,
    GatewaySession? Session,
    string Message);

public enum MoveIntentRateLimitStatus
{
    Accepted,
    MissingAuthenticatedSession,
    RateLimited
}

public sealed record GatewayMessageRateLimitResult(
    GatewayMessageRateLimitStatus Status,
    GatewaySession? Session,
    GatewayRateLimitCategory Category,
    string Message);

public enum GatewayMessageRateLimitStatus
{
    Accepted,
    MissingAuthenticatedSession,
    RateLimited
}

public sealed record ReconnectTokenIssue(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record ReconnectLeaseResult(
    ReconnectLeaseStatus Status,
    GatewaySession? Session,
    SessionLeaseRecord? Lease,
    string Message,
    ReconnectTokenIssue? ReconnectToken = null);

public enum ReconnectLeaseStatus
{
    Reconnected,
    MissingAuthenticatedSession,
    InvalidToken,
    AccountMismatch,
    LeaseExpired,
    LeaseConflict
}

public sealed record GatewayDisconnectResult(
    string? CharacterId,
    bool LeasePreserved,
    DateTimeOffset? LeaseExpiresAtUtc);

public sealed record GatewaySessionStoreDocument(
    List<GatewayWorldSessionRecord> WorldSessions,
    List<SessionLeaseRecord> SessionLeases,
    List<ReconnectTokenRecord> ReconnectTokens);

public sealed record GatewayWorldSessionRecord(
    string ConnectionId,
    string AccountId,
    string AccountPseudonym,
    string? CharacterId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc);

public sealed record ReconnectTokenRecord(
    string TokenHash,
    string AccountId,
    string CharacterId,
    string ConnectionId,
    string PreviousConnectionId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc);
