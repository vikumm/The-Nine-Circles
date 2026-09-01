namespace Divinity.GameGateway;

public static class GameGatewayInfo
{
    public const string ComponentName = "game-gateway";
    public const string Status = "VS-006 game ticket consume";
    public const bool ImplementsWssHandshake = false;
    public const bool RoutesGameplayIntents = false;
    public const bool UsesContractsProto = true;
    public const bool ConsumesGameTickets = true;
}
