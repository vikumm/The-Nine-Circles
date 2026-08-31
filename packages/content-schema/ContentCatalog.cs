namespace Divinity.ContentSchema;

public sealed class ContentCatalog
{
    public required MapDefinition Map { get; init; }
    public required IReadOnlyList<SkillDefinition> Skills { get; init; }
    public required IReadOnlyList<ItemDefinition> Items { get; init; }
    public required IReadOnlyList<LootTableDefinition> LootTables { get; init; }
    public required string ContentHash { get; init; }

    public SkillDefinition? FindSkill(string skillId) =>
        Skills.FirstOrDefault(skill => string.Equals(skill.SkillId, skillId, StringComparison.Ordinal));

    public ItemDefinition? FindItem(string itemId) =>
        Items.FirstOrDefault(item => string.Equals(item.ItemId, itemId, StringComparison.Ordinal));

    public LootTableDefinition? FindLootTable(string lootTableId) =>
        LootTables.FirstOrDefault(table => string.Equals(table.LootTableId, lootTableId, StringComparison.Ordinal));
}

public sealed record ContentFile(string RelativePath, string Json);

public sealed record ContentLoadResult(ContentCatalog? Catalog, IReadOnlyList<string> Errors)
{
    public bool Success => Catalog is not null && Errors.Count == 0;
}

public sealed record ContentDraft(
    MapDefinition? Map,
    IReadOnlyList<SkillDefinition> Skills,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<LootTableDefinition> LootTables);
