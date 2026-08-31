namespace Divinity.ContentSchema;

public static class ContentValidator
{
    private const int CurrentSchemaVersion = 1;
    private static readonly string[] RequiredSkillIds = ["knight_basic_slash", "knight_shield_bash_r1"];
    private static readonly ItemRarity[] RequiredShieldRarities = [ItemRarity.Normal, ItemRarity.Good, ItemRarity.Rare];

    public static IReadOnlyList<string> Validate(ContentDraft draft)
    {
        var errors = new List<string>();

        ValidateMap(draft.Map, errors);
        ValidateSkills(draft.Skills, errors);
        ValidateItems(draft.Items, errors);
        ValidateLootTables(draft.LootTables, draft.Items, errors);

        return errors;
    }

    private static void ValidateMap(MapDefinition? map, List<string> errors)
    {
        if (map is null)
        {
            errors.Add("maps/training-field-01/map.json: map definition is required.");
            return;
        }

        if (map.SchemaVersion != CurrentSchemaVersion)
        {
            errors.Add($"maps/training-field-01/map.json: schemaVersion must be {CurrentSchemaVersion}.");
        }

        if (!IsPresent(map.MapId))
        {
            errors.Add("maps/training-field-01/map.json: mapId is required.");
        }

        if (!IsPresent(map.ContentVersion))
        {
            errors.Add("maps/training-field-01/map.json: contentVersion is required.");
        }

        if (map.Bounds.Width <= 0 || map.Bounds.Height <= 0)
        {
            errors.Add("maps/training-field-01/map.json: bounds width and height must be greater than zero.");
        }

        if (map.TileSize <= 0)
        {
            errors.Add("maps/training-field-01/map.json: tileSize must be greater than zero.");
        }

        var blockedCells = new HashSet<(int X, int Y)>();
        foreach (var cell in map.BlockedCells)
        {
            if (!IsInsideMap(map.Bounds, cell.X, cell.Y))
            {
                errors.Add($"maps/training-field-01/map.json: blocked cell ({cell.X},{cell.Y}) is outside map bounds.");
                continue;
            }

            if (!blockedCells.Add((cell.X, cell.Y)))
            {
                errors.Add($"maps/training-field-01/map.json: blocked cell ({cell.X},{cell.Y}) is duplicated.");
            }
        }

        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in map.Regions)
        {
            if (!IsPresent(region.Id))
            {
                errors.Add("maps/training-field-01/map.json: every region requires an id.");
            }
            else if (!regionIds.Add(region.Id))
            {
                errors.Add($"maps/training-field-01/map.json: region id '{region.Id}' is duplicated.");
            }

            if (region.Kind == RegionKind.Unknown)
            {
                errors.Add($"maps/training-field-01/map.json: region '{region.Id}' requires a known kind.");
            }

            if (!IsRectInsideMap(map.Bounds, region.Bounds))
            {
                errors.Add($"maps/training-field-01/map.json: region '{region.Id}' bounds must be inside map bounds and have positive size.");
            }
        }

        var safeSpawnRegions = map.Regions.Where(region => region.Kind == RegionKind.SafeSpawn).ToArray();
        if (safeSpawnRegions.Length == 0 || map.SafeSpawns.Count == 0)
        {
            errors.Add("maps/training-field-01/map.json: at least one safe spawn region and safeSpawns entry is required.");
        }

        foreach (var safeSpawn in map.SafeSpawns)
        {
            if (!IsPresent(safeSpawn.Id))
            {
                errors.Add("maps/training-field-01/map.json: every safe spawn requires an id.");
            }

            if (!IsInsideMap(map.Bounds, safeSpawn.X, safeSpawn.Y))
            {
                errors.Add($"maps/training-field-01/map.json: safe spawn '{safeSpawn.Id}' is outside map bounds.");
                continue;
            }

            if (blockedCells.Contains((safeSpawn.X, safeSpawn.Y)))
            {
                errors.Add($"maps/training-field-01/map.json: safe spawn '{safeSpawn.Id}' is placed on blocked cell ({safeSpawn.X},{safeSpawn.Y}).");
            }

            if (safeSpawnRegions.Length > 0 && !safeSpawnRegions.Any(region => Contains(region.Bounds, safeSpawn.X, safeSpawn.Y)))
            {
                errors.Add($"maps/training-field-01/map.json: safe spawn '{safeSpawn.Id}' must be inside a safe spawn region.");
            }
        }

        foreach (var spawn in map.Spawns)
        {
            if (!IsPresent(spawn.Id))
            {
                errors.Add("maps/training-field-01/map.json: every spawn requires an id.");
            }

            if (spawn.Kind == SpawnKind.Unknown)
            {
                errors.Add($"maps/training-field-01/map.json: spawn '{spawn.Id}' requires a known kind.");
            }

            if (!IsInsideMap(map.Bounds, spawn.X, spawn.Y))
            {
                errors.Add($"maps/training-field-01/map.json: spawn '{spawn.Id}' is outside map bounds.");
                continue;
            }

            if (blockedCells.Contains((spawn.X, spawn.Y)))
            {
                errors.Add($"maps/training-field-01/map.json: spawn '{spawn.Id}' is placed on blocked cell ({spawn.X},{spawn.Y}).");
            }

            if (IsPresent(spawn.RegionId) && !regionIds.Contains(spawn.RegionId))
            {
                errors.Add($"maps/training-field-01/map.json: spawn '{spawn.Id}' references unknown region '{spawn.RegionId}'.");
            }
        }

        foreach (var trigger in map.Triggers)
        {
            if (!IsPresent(trigger.Id))
            {
                errors.Add("maps/training-field-01/map.json: every trigger requires an id.");
            }

            if (trigger.Kind == TriggerKind.Unknown)
            {
                errors.Add($"maps/training-field-01/map.json: trigger '{trigger.Id}' requires a known kind.");
            }

            if (!IsRectInsideMap(map.Bounds, trigger.Bounds))
            {
                errors.Add($"maps/training-field-01/map.json: trigger '{trigger.Id}' bounds must be inside map bounds and have positive size.");
            }
        }
    }

    private static void ValidateSkills(IReadOnlyList<SkillDefinition> skills, List<string> errors)
    {
        ValidateDuplicateIds(skills.Select(skill => skill.SkillId), "skills/knight", "skillId", errors);

        foreach (var skillId in RequiredSkillIds)
        {
            if (!skills.Any(skill => string.Equals(skill.SkillId, skillId, StringComparison.Ordinal)))
            {
                errors.Add($"skills/knight: required skill '{skillId}' is missing.");
            }
        }

        foreach (var skill in skills)
        {
            var label = $"skills/knight/{skill.SkillId}.json";
            if (skill.SchemaVersion != CurrentSchemaVersion)
            {
                errors.Add($"{label}: schemaVersion must be {CurrentSchemaVersion}.");
            }

            if (!IsPresent(skill.SkillId))
            {
                errors.Add($"{label}: skillId is required.");
            }

            if (!string.Equals(skill.ClassId, "knight", StringComparison.Ordinal))
            {
                errors.Add($"{label}: classId must be 'knight' for VS-004.");
            }

            if (skill.Authority != AuthorityMode.Server)
            {
                errors.Add($"{label}: authority must be 'server'.");
            }

            if (skill.MaxRank <= 0 || skill.RangeCells < 0 || skill.CooldownSeconds < 0 || skill.ResourceCost < 0)
            {
                errors.Add($"{label}: maxRank, rangeCells, cooldownSeconds and resourceCost must be non-negative with maxRank greater than zero.");
            }

            if (skill.Effects.Count == 0)
            {
                errors.Add($"{label}: at least one effect is required.");
            }

            foreach (var effect in skill.Effects.Where(effect => effect.Type == SkillEffectType.Unknown))
            {
                errors.Add($"{label}: effect '{effect.Attribute}' requires a known type.");
            }
        }
    }

    private static void ValidateItems(IReadOnlyList<ItemDefinition> items, List<string> errors)
    {
        ValidateDuplicateIds(items.Select(item => item.ItemId), "items/tier-0", "itemId", errors);

        var shield = items.FirstOrDefault(item => string.Equals(item.ItemId, "knight_wooden_shield_t0", StringComparison.Ordinal));
        if (shield is null)
        {
            errors.Add("items/tier-0: required item 'knight_wooden_shield_t0' is missing.");
            return;
        }

        foreach (var item in items)
        {
            var label = $"items/tier-0/{item.ItemId}.json";
            if (item.SchemaVersion != CurrentSchemaVersion)
            {
                errors.Add($"{label}: schemaVersion must be {CurrentSchemaVersion}.");
            }

            if (item.Authority != AuthorityMode.Server)
            {
                errors.Add($"{label}: authority must be 'server'.");
            }

            if (!string.Equals(item.ClassRestriction, "knight", StringComparison.Ordinal))
            {
                errors.Add($"{label}: classRestriction must be 'knight' for VS-004.");
            }

            if (item.Slot == EquipmentSlot.Unknown)
            {
                errors.Add($"{label}: slot requires a known value.");
            }

            if (item.Tier != 0)
            {
                errors.Add($"{label}: tier must be 0 for VS-004 tier-0 content.");
            }
        }

        foreach (var rarity in RequiredShieldRarities)
        {
            if (!shield.RarityVariants.Any(variant => variant.Rarity == rarity))
            {
                errors.Add($"items/tier-0/knight_wooden_shield_t0.json: rarity variant '{rarity.ToString().ToLowerInvariant()}' is required.");
            }
        }
    }

    private static void ValidateLootTables(IReadOnlyList<LootTableDefinition> lootTables, IReadOnlyList<ItemDefinition> items, List<string> errors)
    {
        ValidateDuplicateIds(lootTables.Select(table => table.LootTableId), "loot-tables", "lootTableId", errors);

        var table = lootTables.FirstOrDefault(lootTable => string.Equals(lootTable.LootTableId, "mob_moss_slime_l1", StringComparison.Ordinal));
        if (table is null)
        {
            errors.Add("loot-tables: required loot table 'mob_moss_slime_l1' is missing.");
        }

        var itemIds = items.Select(item => item.ItemId).Where(IsPresent).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var itemVariants = items
            .Where(item => IsPresent(item.ItemId))
            .GroupBy(item => item.ItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().RarityVariants.Select(variant => variant.Rarity).ToHashSet(),
                StringComparer.Ordinal);

        foreach (var lootTable in lootTables)
        {
            var label = $"loot-tables/{lootTable.LootTableId}.json";
            if (lootTable.SchemaVersion != CurrentSchemaVersion)
            {
                errors.Add($"{label}: schemaVersion must be {CurrentSchemaVersion}.");
            }

            if (lootTable.Authority != AuthorityMode.Server)
            {
                errors.Add($"{label}: authority must be 'server'.");
            }

            if (!IsPresent(lootTable.MobTemplateId))
            {
                errors.Add($"{label}: mobTemplateId is required.");
            }

            if (lootTable.Currency.MinAmount < 0 || lootTable.Currency.MaxAmount < lootTable.Currency.MinAmount)
            {
                errors.Add($"{label}: currency minAmount/maxAmount are inconsistent.");
            }

            if (lootTable.Entries.Count == 0)
            {
                errors.Add($"{label}: at least one loot entry is required.");
            }

            foreach (var entry in lootTable.Entries)
            {
                if (!itemIds.Contains(entry.ItemId))
                {
                    errors.Add($"{label}: loot entry references unknown item '{entry.ItemId}'.");
                    continue;
                }

                if (entry.Rarity == ItemRarity.Unknown)
                {
                    errors.Add($"{label}: loot entry for '{entry.ItemId}' requires a known rarity.");
                }
                else if (!itemVariants[entry.ItemId].Contains(entry.Rarity))
                {
                    errors.Add($"{label}: item '{entry.ItemId}' does not define rarity '{entry.Rarity.ToString().ToLowerInvariant()}'.");
                }

                if (entry.Weight <= 0)
                {
                    errors.Add($"{label}: loot entry for '{entry.ItemId}' must have weight greater than zero.");
                }
            }
        }
    }

    private static void ValidateDuplicateIds(IEnumerable<string> ids, string path, string fieldName, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!IsPresent(id))
            {
                errors.Add($"{path}: {fieldName} is required.");
                continue;
            }

            if (!seen.Add(id))
            {
                errors.Add($"{path}: duplicate {fieldName} '{id}'.");
            }
        }
    }

    private static bool IsInsideMap(GridBounds bounds, int x, int y) =>
        bounds.Width > 0 && bounds.Height > 0 && x >= 0 && y >= 0 && x < bounds.Width && y < bounds.Height;

    private static bool IsRectInsideMap(GridBounds bounds, GridRect rect) =>
        bounds.Width > 0
        && bounds.Height > 0
        && rect.Width > 0
        && rect.Height > 0
        && rect.X >= 0
        && rect.Y >= 0
        && rect.X + rect.Width <= bounds.Width
        && rect.Y + rect.Height <= bounds.Height;

    private static bool Contains(GridRect rect, int x, int y) =>
        x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;

    private static bool IsPresent(string value) => !string.IsNullOrWhiteSpace(value);
}
