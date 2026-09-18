using Divinity.GameRules.Monsters;

namespace Divinity.GameRules.Rewards;

public interface IRewardRandomSource
{
    int NextInclusive(int minValue, int maxValue);
    int NextBasisPoint();
}

public sealed class SystemRewardRandomSource : IRewardRandomSource
{
    private readonly Random _random;

    public SystemRewardRandomSource()
        : this(Random.Shared)
    {
    }

    public SystemRewardRandomSource(Random random)
    {
        _random = random;
    }

    public int NextInclusive(int minValue, int maxValue) =>
        _random.Next(minValue, maxValue + 1);

    public int NextBasisPoint() => _random.Next(0, 10_000);
}

public sealed record RewardLootSelection(
    int CurrencyAmount,
    RewardItemDrop? ItemDrop);

public sealed record RewardItemDrop(
    string ItemTemplateId,
    string Rarity,
    bool Durable,
    int MaxDurability);

public static class MossSlimeRewardCatalog
{
    public const string CatalogVersion = "vs016.1";
    public const string LootTableId = "mob_moss_slime_l1";
    public const string CurrencyId = "copper";
    public const int CurrencyMinAmount = 1;
    public const int CurrencyMaxAmount = 3;
    public const string SmallPotionTemplateId = "small_potion_t0";
    public const string WoodenShieldTemplateId = "knight_wooden_shield_t0";
    public const string NormalRarity = "normal";
    public const string GoodRarity = "good";
    public const string RareRarity = "rare";
    public const int WoodenShieldMaxDurability = 20;

    public static RewardLootSelection Roll(IRewardRandomSource random)
    {
        var currency = random.NextInclusive(CurrencyMinAmount, CurrencyMaxAmount);
        var roll = random.NextBasisPoint();

        RewardItemDrop? item = roll switch
        {
            < 50 => new RewardItemDrop(WoodenShieldTemplateId, RareRarity, Durable: true, WoodenShieldMaxDurability),
            < 250 => new RewardItemDrop(WoodenShieldTemplateId, GoodRarity, Durable: true, WoodenShieldMaxDurability),
            < 1_050 => new RewardItemDrop(WoodenShieldTemplateId, NormalRarity, Durable: true, WoodenShieldMaxDurability),
            < 3_050 => new RewardItemDrop(SmallPotionTemplateId, NormalRarity, Durable: false, MaxDurability: 0),
            _ => null
        };

        return new RewardLootSelection(currency, item);
    }

    public static int KillXp => MossSlimeCatalog.Level1.Xp;
}
