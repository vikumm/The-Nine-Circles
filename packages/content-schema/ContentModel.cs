namespace Divinity.ContentSchema;

public sealed class MapDefinition
{
    public int SchemaVersion { get; init; }
    public string MapId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ContentVersion { get; init; } = string.Empty;
    public GridBounds Bounds { get; init; } = new();
    public decimal TileSize { get; init; }
    public List<GridCell> BlockedCells { get; init; } = [];
    public List<MapRegion> Regions { get; init; } = [];
    public List<MapSpawn> Spawns { get; init; } = [];
    public List<SafeSpawn> SafeSpawns { get; init; } = [];
    public List<MapTrigger> Triggers { get; init; } = [];
}

public sealed class GridBounds
{
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class GridCell
{
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed class GridRect
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class MapRegion
{
    public string Id { get; init; } = string.Empty;
    public RegionKind Kind { get; init; }
    public GridRect Bounds { get; init; } = new();
}

public sealed class MapSpawn
{
    public string Id { get; init; } = string.Empty;
    public SpawnKind Kind { get; init; }
    public string EntityTemplateId { get; init; } = string.Empty;
    public string RegionId { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed class SafeSpawn
{
    public string Id { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed class MapTrigger
{
    public string Id { get; init; } = string.Empty;
    public TriggerKind Kind { get; init; }
    public GridRect Bounds { get; init; } = new();
}

public sealed class SkillDefinition
{
    public int SchemaVersion { get; init; }
    public string SkillId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public AuthorityMode Authority { get; init; }
    public int MaxRank { get; init; }
    public decimal RangeCells { get; init; }
    public decimal CooldownSeconds { get; init; }
    public int ResourceCost { get; init; }
    public List<SkillEffectDefinition> Effects { get; init; } = [];
    public List<SkillRankDefinition> RankProgression { get; init; } = [];
}

public sealed class SkillEffectDefinition
{
    public SkillEffectType Type { get; init; }
    public string Attribute { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal DurationSeconds { get; init; }
}

public sealed class SkillRankDefinition
{
    public int Rank { get; init; }
    public int RequiredSkillXp { get; init; }
}

public sealed class ItemDefinition
{
    public int SchemaVersion { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ClassRestriction { get; init; } = string.Empty;
    public EquipmentSlot Slot { get; init; }
    public int Tier { get; init; }
    public AuthorityMode Authority { get; init; }
    public List<ItemRarityVariant> RarityVariants { get; init; } = [];
}

public sealed class ItemRarityVariant
{
    public ItemRarity Rarity { get; init; }
    public Dictionary<string, decimal> Attributes { get; init; } = [];
}

public sealed class LootTableDefinition
{
    public int SchemaVersion { get; init; }
    public string LootTableId { get; init; } = string.Empty;
    public string MobTemplateId { get; init; } = string.Empty;
    public AuthorityMode Authority { get; init; }
    public LootCurrency Currency { get; init; } = new();
    public List<LootEntryDefinition> Entries { get; init; } = [];
}

public sealed class LootCurrency
{
    public string CurrencyId { get; init; } = string.Empty;
    public int MinAmount { get; init; }
    public int MaxAmount { get; init; }
}

public sealed class LootEntryDefinition
{
    public string ItemId { get; init; } = string.Empty;
    public ItemRarity Rarity { get; init; }
    public int Weight { get; init; }
}

public enum AuthorityMode
{
    Unknown = 0,
    Server = 1
}

public enum RegionKind
{
    Unknown = 0,
    SafeSpawn = 1,
    TrainingGround = 2,
    CombatZone = 3
}

public enum SpawnKind
{
    Unknown = 0,
    Player = 1,
    Monster = 2
}

public enum TriggerKind
{
    Unknown = 0,
    TrainingExit = 1,
    CombatAreaEnter = 2
}

public enum SkillEffectType
{
    Unknown = 0,
    Damage = 1,
    Stun = 2,
    SkillXp = 3
}

public enum EquipmentSlot
{
    Unknown = 0,
    OffHand = 1
}

public enum ItemRarity
{
    Unknown = 0,
    Normal = 1,
    Good = 2,
    Rare = 3
}
