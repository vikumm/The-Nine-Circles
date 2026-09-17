using Divinity.GameRules.Characters;

namespace Divinity.GameRules.Combat;

public static class BasicSlashCatalog
{
    public const string SkillId = "knight_basic_slash";
    public const double RangeUnits = 1.5d;
    public const int ResourceCostMp = 0;
    public const decimal Coefficient = 1.0m;
    public const int BasePower = 2;
    public const decimal CriticalMultiplier = 1.5m;
    public static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(800);
}

public interface ICombatRandomSource
{
    double NextVariance();
    bool RollCritical(decimal criticalChancePercent);
}

public sealed class SystemCombatRandomSource : ICombatRandomSource
{
    private readonly Random _random;

    public SystemCombatRandomSource()
        : this(Random.Shared)
    {
    }

    public SystemCombatRandomSource(Random random)
    {
        _random = random;
    }

    public double NextVariance() => 0.95d + _random.NextDouble() * 0.10d;

    public bool RollCritical(decimal criticalChancePercent) =>
        criticalChancePercent > 0 && (decimal)_random.NextDouble() * 100m < criticalChancePercent;
}

public sealed record BasicSlashDamageRoll(
    int RawDamage,
    decimal Mitigation,
    double Variance,
    bool Critical,
    int Damage);

public static class BasicSlashDamageCalculator
{
    public static BasicSlashDamageRoll Calculate(KnightStats attacker, int targetDefense, ICombatRandomSource random)
    {
        var rawDamage = (int)Math.Floor(attacker.Attack * BasicSlashCatalog.Coefficient + BasicSlashCatalog.BasePower);
        var mitigation = targetDefense <= 0
            ? 0m
            : (decimal)targetDefense / (targetDefense + 100m);
        var variance = random.NextVariance();
        var mitigated = rawDamage * (1m - mitigation) * (decimal)variance;
        var damage = Math.Max(1, (int)Math.Floor(mitigated));
        var critical = random.RollCritical(attacker.CriticalChancePercent);
        if (critical)
        {
            damage = Math.Max(1, (int)Math.Floor(damage * BasicSlashCatalog.CriticalMultiplier));
        }

        return new BasicSlashDamageRoll(rawDamage, mitigation, variance, critical, damage);
    }
}
