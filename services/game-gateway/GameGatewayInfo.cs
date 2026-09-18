namespace Divinity.GameGateway;

public static class GameGatewayInfo
{
    public const string ComponentName = "game-gateway";
    public const string Status = "VS-020 hardened observable gateway";
    public const bool ImplementsWssHandshake = true;
    public const bool RoutesGameplayIntents = true;
    public const bool RoutesAttackIntents = true;
    public const bool RoutesCastIntents = true;
    public const bool RoutesInventoryIntents = true;
    public const bool SupportsReconnect = true;
    public const bool UsesContractsProto = true;
    public const bool ConsumesGameTickets = true;
    public const bool VerifiesCharacterOwnership = true;
    public const bool ImplementsCategoryRateLimits = true;
    public const bool ExposesTelemetry = true;
    public const int MaxEnvelopeBytes = 65536;
}
