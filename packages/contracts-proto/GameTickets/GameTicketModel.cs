namespace Divinity.ContractsProto.GameTickets;

public static class GameTicketDefaults
{
    public const string DefaultBuildId = "vs1-dev";
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);
}

public sealed record GameTicketIssueCommand(
    string? AccountId,
    string BuildId,
    uint ProtocolVersion,
    string Nonce);

public sealed record GameTicketIssueResult(
    GameTicketIssueStatus Status,
    string? GameTicket,
    DateTimeOffset? ExpiresAtUtc,
    string Message)
{
    public bool Success => Status == GameTicketIssueStatus.Issued;
}

public enum GameTicketIssueStatus
{
    Issued,
    NotAuthenticated,
    InvalidRequest,
    UnsupportedProtocolVersion
}

public sealed record GameTicketConsumeCommand(
    string GameTicket,
    string BuildId,
    uint ProtocolVersion,
    string Nonce);

public sealed record GameTicketConsumeResult(
    GameTicketConsumeStatus Status,
    StoredGameTicket? Ticket,
    string Message)
{
    public bool Success => Status == GameTicketConsumeStatus.Consumed;
}

public enum GameTicketConsumeStatus
{
    Consumed,
    MissingTicket,
    MalformedTicket,
    InvalidTicket,
    ExpiredTicket,
    ReusedTicket,
    BuildMismatch,
    ProtocolMismatch,
    NonceMismatch
}

public sealed record StoredGameTicket(
    string AccountId,
    string BuildId,
    uint ProtocolVersion,
    string Nonce,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record GameTicketStoreConsumeResult(
    GameTicketStoreConsumeStatus Status,
    StoredGameTicket? Ticket);

public enum GameTicketStoreConsumeStatus
{
    Consumed,
    NotFound,
    Expired,
    Reused
}
