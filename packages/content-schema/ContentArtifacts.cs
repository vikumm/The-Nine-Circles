namespace Divinity.ContentSchema;

public static class ContentArtifactFactory
{
    public const int ChunkSize = 16;

    public static ClientContentArtifact CreateClientArtifact(ContentCatalog catalog) => new()
    {
        SchemaVersion = 1,
        ContentVersion = catalog.Map.ContentVersion,
        ContentHash = catalog.ContentHash,
        Map = new ClientMapArtifact
        {
            MapId = catalog.Map.MapId,
            StableId = catalog.Map.StableId,
            Name = catalog.Map.Name,
            Bounds = catalog.Map.Bounds,
            TileSize = catalog.Map.TileSize,
            BlockedCells = catalog.Map.BlockedCells,
            Regions = catalog.Map.Regions,
            SafeSpawns = catalog.Map.SafeSpawns,
            Triggers = catalog.Map.Triggers,
            Chunks = CreateChunkGrid(catalog.Map)
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
        Chunks = CreateChunkGrid(catalog.Map),
        Skills = catalog.Skills.OrderBy(skill => skill.SkillId, StringComparer.Ordinal).ToList(),
        Items = catalog.Items.OrderBy(item => item.ItemId, StringComparer.Ordinal).ToList(),
        LootTables = catalog.LootTables.OrderBy(table => table.LootTableId, StringComparer.Ordinal).ToList()
    };

    private static MapChunkGrid CreateChunkGrid(MapDefinition map)
    {
        var columns = map.Bounds.Width / ChunkSize;
        var rows = map.Bounds.Height / ChunkSize;
        var blockedCells = map.BlockedCells
            .Select(cell => (cell.X, cell.Y))
            .ToHashSet();
        var chunks = new List<MapChunk>();

        for (var chunkY = 0; chunkY < rows; chunkY++)
        {
            for (var chunkX = 0; chunkX < columns; chunkX++)
            {
                var originX = chunkX * ChunkSize;
                var originY = chunkY * ChunkSize;
                var chunkBlockedCells = new List<GridCell>();

                for (var y = originY; y < originY + ChunkSize; y++)
                {
                    for (var x = originX; x < originX + ChunkSize; x++)
                    {
                        if (blockedCells.Contains((x, y)))
                        {
                            chunkBlockedCells.Add(new GridCell { X = x, Y = y });
                        }
                    }
                }

                chunks.Add(new MapChunk
                {
                    X = originX,
                    Y = originY,
                    Width = ChunkSize,
                    Height = ChunkSize,
                    BlockedCells = chunkBlockedCells
                });
            }
        }

        return new MapChunkGrid
        {
            ChunkSize = ChunkSize,
            Columns = columns,
            Rows = rows,
            Chunks = chunks
        };
    }
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
    public string StableId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public GridBounds Bounds { get; init; } = new();
    public decimal TileSize { get; init; }
    public List<GridCell> BlockedCells { get; init; } = [];
    public List<MapRegion> Regions { get; init; } = [];
    public List<SafeSpawn> SafeSpawns { get; init; } = [];
    public List<MapTrigger> Triggers { get; init; } = [];
    public MapChunkGrid Chunks { get; init; } = new();
}

public sealed class ServerContentArtifact
{
    public int SchemaVersion { get; init; }
    public string ContentVersion { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public MapDefinition Map { get; init; } = new();
    public MapChunkGrid Chunks { get; init; } = new();
    public List<SkillDefinition> Skills { get; init; } = [];
    public List<ItemDefinition> Items { get; init; } = [];
    public List<LootTableDefinition> LootTables { get; init; } = [];
}

public sealed class MapChunkGrid
{
    public int ChunkSize { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public List<MapChunk> Chunks { get; init; } = [];
}

public sealed class MapChunk
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public List<GridCell> BlockedCells { get; init; } = [];
}
