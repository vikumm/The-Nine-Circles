namespace Divinity.GameGateway.Session;

public static class GatewaySessionDefaults
{
    public const int AnonymousHandshakeLimit = 3;
    public const int MoveIntentLimit = 20;
    public const string StubMapId = "training-field-01";
    public const string StubChannelId = "gateway-local";
    public const string StubContentHash = "6529839f9a9e7a0f1dc939a9e72fa7b6938f588f24317b5df64fc1507136a89e";
    public static readonly TimeSpan AnonymousHandshakeWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MoveIntentWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);
}

public sealed class GatewaySession
{
    private readonly Queue<DateTimeOffset> _moveIntentAttempts = new();

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

    public bool TryAcquireMoveIntent(DateTimeOffset nowUtc)
    {
        while (_moveIntentAttempts.Count > 0
            && nowUtc - _moveIntentAttempts.Peek() >= GatewaySessionDefaults.MoveIntentWindow)
        {
            _moveIntentAttempts.Dequeue();
        }

        if (_moveIntentAttempts.Count >= GatewaySessionDefaults.MoveIntentLimit)
        {
            return false;
        }

        _moveIntentAttempts.Enqueue(nowUtc);
        LastSeenAtUtc = nowUtc;
        return true;
    }
}

public sealed record SessionLeaseRecord(
    string CharacterId,
    string ConnectionId,
    string AccountPseudonym,
    DateTimeOffset ExpiresAtUtc);

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
    string Message);

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
