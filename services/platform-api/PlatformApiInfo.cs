namespace Divinity.PlatformApi;

public static class PlatformApiInfo
{
    public const string ComponentName = "platform-api";
    public const string Status = "VS-020 hardened observable platform API";
    public const bool ImplementsDomainPersistence = true;
    public const bool IssuesGameTickets = true;
    public const bool CreatesKnight = true;
    public const bool ExposesTelemetry = true;
    public const bool AvoidsSecretLogs = true;
}
