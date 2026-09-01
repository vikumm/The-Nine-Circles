namespace Divinity.ContractsProto.GameTickets;

public static class GameTicketStoreFactory
{
    public static IGameTicketStore CreateFromEnvironment() => FileGameTicketStore.FromEnvironment();
}
