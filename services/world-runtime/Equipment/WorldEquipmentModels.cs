using Divinity.Contracts.V1;
using Divinity.GameRules.Equipment;

namespace Divinity.WorldRuntime.Equipment;

public sealed record WorldEquippedItem(
    EquipmentSlot Slot,
    string ItemInstanceId,
    string TemplateId,
    EquipmentRarity Rarity,
    int Durability,
    int MaxDurability,
    bool Durable)
{
    public bool AttributesActive => !Durable || Durability > 0;
}

public sealed record WorldEquipmentDurabilityResult(
    string CharacterId,
    IReadOnlyList<WorldEquippedItem> Equipment,
    InventoryDelta InventoryDelta);
