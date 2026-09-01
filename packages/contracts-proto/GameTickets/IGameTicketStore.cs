namespace Divinity.ContractsProto.GameTickets;

public interface IGameTicketStore
{
    Task StoreAsync(string ticketHash, StoredGameTicket ticket, TimeSpan ttl, CancellationToken cancellationToken);

    Task<GameTicketStoreConsumeResult> ConsumeAsync(string ticketHash, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
