namespace Divinity.GameRules.Equipment;

public enum EquipmentRarity
{
    Normal,
    Good,
    Rare
}

public sealed record EquipmentAttributeBonus(int Defense, int Block);

public static class WoodenShieldCatalog
{
    public const string TemplateId = "knight_wooden_shield_t0";
    public const int MaxDurability = 20;

    public static EquipmentAttributeBonus GetAttributes(EquipmentRarity rarity) =>
        rarity switch
        {
            EquipmentRarity.Rare => new EquipmentAttributeBonus(Defense: 5, Block: 2),
            EquipmentRarity.Good => new EquipmentAttributeBonus(Defense: 4, Block: 1),
            _ => new EquipmentAttributeBonus(Defense: 3, Block: 0)
        };

    public static EquipmentAttributeBonus GetActiveAttributes(EquipmentRarity rarity, int durability) =>
        durability > 0 ? GetAttributes(rarity) : new EquipmentAttributeBonus(Defense: 0, Block: 0);
}
