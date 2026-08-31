namespace Divinity.ContentSchema;

public static class ContentArtifactFactory
{
    public static ClientContentArtifact CreateClientArtifact(ContentCatalog catalog) => new()
    {
        SchemaVersion = 1,
        ContentVersion = catalog.Map.ContentVersion,
        ContentHash = catalog.ContentHash,
        Map = new ClientMapArtifact
        {
            MapId = catalog.Map.MapId,
            Name = catalog.Map.Name,
            Bounds = catalog.Map.Bounds,
            TileSize = catalog.Map.TileSize,
            BlockedCells = catalog.Map.BlockedCells,
            Regions = catalog.Map.Regions,
            SafeSpawns = catalog.Map.SafeSpawns,
            Triggers = catalog.Map.Triggers
        },
        SkillIds = catalog.Skills.Select(skill => skill.SkillId).OrderBy(skillId => skillId, StringComparer.Ordinal).ToList(),
        ItemIds = catalog.Items.Select(item => item.ItemId).OrderBy(itemId => itemId, StringComparer.Ordinal).ToList(),
        LootTableIds = catalog.LootTables.Select(table => table.LootTableId).OrderBy(lootTableId => lootTableId, StringComparer.Ordinal).ToList()
    };

    public static ServerContentArtifact CreateServerArtifact(ContentCatalog catalog) => new()
    {
        SchemaVersion = 1,
        ContentVersion = catalog.Map.ContentVersion,
        ContentHash = catalog.ContentHash,
        Map = catalog.Map,
        Skills = catalog.Skills.OrderBy(skill => skill.SkillId, StringComparer.Ordinal).ToList(),
        Items = catalog.Items.OrderBy(item => item.ItemId, StringComparer.Ordinal).ToList(),
        LootTables = catalog.LootTables.OrderBy(table => table.LootTableId, StringComparer.Ordinal).ToList()
    };
}

public sealed class ClientContentArtifact
{
    public int SchemaVersion { get; init; }
    public string ContentVersion { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public ClientMapArtifact Map { get; init; } = new();
    public List<string> SkillIds { get; init; } = [];
    public List<string> ItemIds { get; init; } = [];
    public List<string> LootTableIds { get; init; } = [];
}

public sealed class ClientMapArtifact
{
    public string MapId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public GridBounds Bounds { get; init; } = new();
    public decimal TileSize { get; init; }
    public List<GridCell> BlockedCells { get; init; } = [];
    public List<MapRegion> Regions { get; init; } = [];
    public List<SafeSpawn> SafeSpawns { get; init; } = [];
    public List<MapTrigger> Triggers { get; init; } = [];
}

public sealed class ServerContentArtifact
{
    public int SchemaVersion { get; init; }
    public string ContentVersion { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public MapDefinition Map { get; init; } = new();
    public List<SkillDefinition> Skills { get; init; } = [];
    public List<ItemDefinition> Items { get; init; } = [];
    public List<LootTableDefinition> LootTables { get; init; } = [];
}
