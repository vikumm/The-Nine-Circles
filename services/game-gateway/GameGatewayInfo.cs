namespace Divinity.GameGateway;

public static class GameGatewayInfo
{
    public const string ComponentName = "game-gateway";
    public const string Status = "VS-013 basic attack gateway";
    public const bool ImplementsWssHandshake = true;
    public const bool RoutesGameplayIntents = true;
    public const bool RoutesAttackIntents = true;
    public const bool UsesContractsProto = true;
    public const bool ConsumesGameTickets = true;
    public const bool VerifiesCharacterOwnership = true;
}
