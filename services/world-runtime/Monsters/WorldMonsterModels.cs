using Divinity.Contracts.V1;
using Divinity.GameRules.Monsters;

namespace Divinity.WorldRuntime.Monsters;

public sealed record WorldMonsterTickResult(
    WorldSnapshot Snapshot,
    IReadOnlyList<CombatEvent> CombatEvents,
    IReadOnlyList<RewardGranted> RewardEvents);

public sealed record WorldMonsterDeathResult(
    bool Success,
    string Message,
    string? KillId,
    IReadOnlyList<RewardGranted> RewardEvents);

public sealed record WorldMonsterCombatTarget(
    string EntityId,
    string MapId,
    string ChannelId,
    string TemplateId,
    double X,
    double Y,
    int Hp,
    int Defense,
    bool Active);

public sealed record WorldMonsterDamageResult(
    bool Success,
    string Message,
    int DamageApplied,
    int TargetHp,
    string? KillId);

public sealed class WorldMonsterState
{
    internal WorldMonsterState(
        string entityId,
        string spawnId,
        string templateId,
        int level,
        double spawnX,
        double spawnY,
        DateTimeOffset nowUtc)
    {
        EntityId = entityId;
        SpawnId = spawnId;
        TemplateId = templateId;
        Level = level;
        SpawnX = spawnX;
        SpawnY = spawnY;
        X = spawnX;
        Y = spawnY;
        LastPathRecalculatedAtUtc = nowUtc;
        NextWanderAtUtc = nowUtc + MossSlimeCatalog.WanderInterval;
    }

    public string EntityId { get; }
    public string SpawnId { get; }
    public string TemplateId { get; }
    public int Level { get; }
    public double SpawnX { get; }
    public double SpawnY { get; }
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public int Hp { get; internal set; } = MossSlimeCatalog.Level1.MaxHp;
    public MonsterAiState AiState { get; internal set; } = MonsterAiState.Idle;
    public string? TargetCharacterId { get; internal set; }
    public DateTimeOffset LastPathRecalculatedAtUtc { get; internal set; }
    public DateTimeOffset NextWanderAtUtc { get; internal set; }
    public DateTimeOffset NextAttackAtUtc { get; internal set; }
    public DateTimeOffset? RespawnAtUtc { get; internal set; }
    public string? LastKillId { get; internal set; }
    public int PathRecalculationCount { get; internal set; }
    public bool Active => AiState != MonsterAiState.Dead;
}

public sealed class WorldMonsterPlayerState
{
    internal WorldMonsterPlayerState(string characterId, double x, double y, int hp, bool connected, bool alive)
    {
        CharacterId = characterId;
        X = x;
        Y = y;
        Hp = hp;
        Connected = connected;
        Alive = alive;
    }

    public string CharacterId { get; }
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public int Hp { get; internal set; }
    public bool Connected { get; internal set; }
    public bool Alive { get; internal set; }
    public bool IsValidTarget => Connected && Alive && Hp > 0;
}
