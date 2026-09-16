namespace Divinity.GameRules.Characters;

public static class KnightCatalog
{
    public const string CatalogVersion = "vs008.1";
    public const string Vocation = "Knight";
    public const string MapId = "training-field-01";
    public const string ChannelId = "training-field-local";
    public const string ContentHash = "6529839f9a9e7a0f1dc939a9e72fa7b6938f588f24317b5df64fc1507136a89e";
    public static readonly CharacterPosition SafeSpawn = new(8, 8);

    public static KnightStats LevelOneStats { get; } = new(
        Level: 1,
        MaxHp: 120,
        MaxMp: 40,
        Attack: 12,
        Defense: 8,
        CriticalChancePercent: 5,
        CriticalMultiplier: 1.5m,
        SpeedUnitsPerSecond: 4.5m,
        HpRegenPerSecond: 1,
        MpRegenPerSecond: 2);
}
