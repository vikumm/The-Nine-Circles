namespace Divinity.WorldRuntime;

public static class WorldRuntimeInfo
{
    public const string ComponentName = "world-runtime";
    public const string Status = "VS-020 observable world runtime";
    public const bool UsesAuthoritativeMapArtifact = true;
    public const bool ImplementsMovement = true;
    public const bool ImplementsMonsterAi = true;
    public const bool ImplementsCombat = true;
    public const bool ImplementsShieldBash = true;
    public const bool ImplementsDeathRespawn = true;
    public const bool AppliesEquipmentDurabilityLoss = true;
    public const bool ImplementsRewardTransactions = true;
    public const bool SupportsReconnectState = true;
    public const bool UsesContractsProto = true;
    public const bool ExposesTelemetry = true;
    public const bool SupportsSoakShutdownCheckpoint = true;
}
