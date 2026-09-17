namespace Divinity.GameRules.Monsters;

public enum MonsterAiState
{
    Idle,
    Wander,
    Chase,
    Attack,
    Return,
    Dead
}

public sealed record MonsterStats(
    string TemplateId,
    int Level,
    int MaxHp,
    int Attack,
    int Defense,
    double SpeedUnitsPerSecond,
    double AggroRadius,
    double LeashRadius,
    double AttackRange,
    TimeSpan AttackCooldown,
    TimeSpan RespawnDelay,
    int Xp);

public static class MossSlimeCatalog
{
    public const string TemplateId = "mob_moss_slime_l1";
    public const string MapId = "map_training_field_01";
    public const string BasicAttackSkillId = "moss_slime_basic_attack";
    public const int MaxActiveInstances = 30;
    public static readonly TimeSpan PathRecalculationInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan WanderInterval = TimeSpan.FromSeconds(2);

    public static readonly MonsterStats Level1 = new(
        TemplateId,
        Level: 1,
        MaxHp: 45,
        Attack: 6,
        Defense: 2,
        SpeedUnitsPerSecond: 2.8d,
        AggroRadius: 6d,
        LeashRadius: 10d,
        AttackRange: 1.1d,
        AttackCooldown: TimeSpan.FromSeconds(1.5d),
        RespawnDelay: TimeSpan.FromSeconds(8),
        Xp: 20);

    public static bool IsMossSlime(string templateId) =>
        string.Equals(templateId, TemplateId, StringComparison.Ordinal);
}
