using Divinity.Contracts.V1;
using Divinity.GameRules.Equipment;

namespace Divinity.WorldRuntime.Equipment;

public sealed class WorldEquipmentRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<EquipmentSlot, WorldEquippedItem>> _loadoutsByCharacter = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _inventoryVersionsByCharacter = new(StringComparer.Ordinal);

    public WorldEquippedItem EquipWoodenShieldForTesting(
        string characterId,
        string itemInstanceId,
        EquipmentRarity rarity,
        int durability)
    {
        var clampedDurability = Math.Max(0, Math.Min(durability, WoodenShieldCatalog.MaxDurability));
        var item = new WorldEquippedItem(
            EquipmentSlot.OffHand,
            itemInstanceId,
            WoodenShieldCatalog.TemplateId,
            rarity,
            clampedDurability,
            WoodenShieldCatalog.MaxDurability,
            Durable: true);

        lock (_gate)
        {
            if (!_loadoutsByCharacter.TryGetValue(characterId, out var loadout))
            {
                loadout = new Dictionary<EquipmentSlot, WorldEquippedItem>();
                _loadoutsByCharacter[characterId] = loadout;
            }

            loadout[item.Slot] = item;
            _inventoryVersionsByCharacter[characterId] = Math.Max(1, GetInventoryVersionUnsafe(characterId));
        }

        return item;
    }

    public IReadOnlyList<WorldEquippedItem> GetEquipment(string characterId)
    {
        lock (_gate)
        {
            return GetEquipmentUnsafe(characterId);
        }
    }

    public int GetDefenseBonus(string characterId)
    {
        lock (_gate)
        {
            if (!_loadoutsByCharacter.TryGetValue(characterId, out var loadout))
            {
                return 0;
            }

            return loadout.Values.Sum(item =>
                string.Equals(item.TemplateId, WoodenShieldCatalog.TemplateId, StringComparison.Ordinal)
                    ? WoodenShieldCatalog.GetActiveAttributes(item.Rarity, item.Durability).Defense
                    : 0);
        }
    }

    public WorldEquipmentDurabilityResult ApplyDeathDurabilityLoss(string characterId)
    {
        lock (_gate)
        {
            if (!_loadoutsByCharacter.TryGetValue(characterId, out var loadout))
            {
                var emptyDelta = CreateInventoryDelta(characterId, Array.Empty<WorldEquippedItem>());
                return new WorldEquipmentDurabilityResult(characterId, Array.Empty<WorldEquippedItem>(), emptyDelta);
            }

            foreach (var slot in loadout.Keys.ToArray())
            {
                var item = loadout[slot];
                if (item.Durable)
                {
                    loadout[slot] = item with { Durability = Math.Max(0, item.Durability - 1) };
                }
            }

            _inventoryVersionsByCharacter[characterId] = GetInventoryVersionUnsafe(characterId) + 1;
            var equipment = GetEquipmentUnsafe(characterId);
            return new WorldEquipmentDurabilityResult(characterId, equipment, CreateInventoryDelta(characterId, equipment));
        }
    }

    private IReadOnlyList<WorldEquippedItem> GetEquipmentUnsafe(string characterId) =>
        _loadoutsByCharacter.TryGetValue(characterId, out var loadout)
            ? loadout.Values
                .OrderBy(item => item.Slot)
                .Select(item => item with { })
                .ToArray()
            : Array.Empty<WorldEquippedItem>();

    private ulong GetInventoryVersionUnsafe(string characterId) =>
        _inventoryVersionsByCharacter.TryGetValue(characterId, out var version) ? version : 0;

    private InventoryDelta CreateInventoryDelta(string characterId, IReadOnlyList<WorldEquippedItem> equipment)
    {
        var delta = new InventoryDelta
        {
            InventoryVersion = GetInventoryVersionUnsafe(characterId)
        };

        foreach (var item in equipment)
        {
            delta.Equipment.Add(new EquipmentItem
            {
                Slot = item.Slot,
                ItemInstanceId = item.ItemInstanceId,
                Durability = (uint)item.Durability,
                MaxDurability = (uint)item.MaxDurability,
                AttributesActive = item.AttributesActive
            });
        }

        return delta;
    }
}
