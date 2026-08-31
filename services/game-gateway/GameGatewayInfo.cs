namespace Divinity.GameGateway;

public static class GameGatewayInfo
{
    public const string ComponentName = "game-gateway";
    public const string Status = "VS-003 protocol smoke only";
    public const bool ImplementsWssHandshake = false;
    public const bool RoutesGameplayIntents = false;
    public const bool UsesContractsProto = true;
}
