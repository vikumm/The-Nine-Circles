namespace Divinity.ContractsProto.GameTickets;

public interface IGameTicketAuditSink
{
    void Record(string message);
}

public sealed class InMemoryGameTicketAuditSink : IGameTicketAuditSink
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Record(string message) => _entries.Add(message);
}

public sealed class NullGameTicketAuditSink : IGameTicketAuditSink
{
    public static NullGameTicketAuditSink Instance { get; } = new();

    private NullGameTicketAuditSink()
    {
    }

    public void Record(string message)
    {
    }
}
