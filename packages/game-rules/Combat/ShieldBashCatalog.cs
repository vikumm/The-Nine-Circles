using Divinity.GameRules.Characters;

namespace Divinity.GameRules.Combat;

public static class ShieldBashCatalog
{
    public const string SkillId = "knight_shield_bash_r1";
    public const string StunEffectId = "stun";
    public const double RangeUnits = 1.4d;
    public const int ResourceCostMp = 10;
    public const decimal Coefficient = 1.2m;
    public const int BasePower = 8;
    public const int SkillXpPerValidHit = 1;
    public const int MaxRank = 2;
    public const int RankTwoRequiredXp = 20;
    public const decimal CriticalMultiplier = 1.5m;
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StunDuration = TimeSpan.FromMilliseconds(1250);

    public static int ResolveRank(int skillXp) =>
        skillXp >= RankTwoRequiredXp ? 2 : 1;
}

public sealed record ShieldBashDamageRoll(
    int RawDamage,
    decimal Mitigation,
    double Variance,
    bool Critical,
    int Damage);

public static class ShieldBashDamageCalculator
{
    public static ShieldBashDamageRoll Calculate(KnightStats attacker, int targetDefense, ICombatRandomSource random)
    {
        var rawDamage = (int)Math.Floor(attacker.Attack * ShieldBashCatalog.Coefficient + ShieldBashCatalog.BasePower);
        var mitigation = targetDefense <= 0
            ? 0m
            : (decimal)targetDefense / (targetDefense + 100m);
        var variance = random.NextVariance();
        var mitigated = rawDamage * (1m - mitigation) * (decimal)variance;
        var damage = Math.Max(1, (int)Math.Floor(mitigated));
        var critical = random.RollCritical(attacker.CriticalChancePercent);
        if (critical)
        {
            damage = Math.Max(1, (int)Math.Floor(damage * ShieldBashCatalog.CriticalMultiplier));
        }

        return new ShieldBashDamageRoll(rawDamage, mitigation, variance, critical, damage);
    }
}
