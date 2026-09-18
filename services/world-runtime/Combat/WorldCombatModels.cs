using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;

namespace Divinity.WorldRuntime.Combat;

public sealed record WorldCombatActor(
    string EntityId,
    string MapId,
    string ChannelId,
    CharacterPosition Position,
    string Vocation,
    KnightStats Stats,
    WorldCombatActorState State);

public enum WorldCombatActorState
{
    Alive,
    Dead,
    Stunned
}

public sealed record WorldBasicAttackResult(
    WorldBasicAttackStatus Status,
    string Message,
    ulong AckSequence,
    ErrorCode ErrorCode,
    CombatEvent? CombatEvent,
    SkillStateChanged? SkillStateChanged,
    RewardGranted? RewardGranted = null,
    InventoryDelta? InventoryDelta = null,
    CharacterProgressed? RewardProgressed = null)
{
    public bool Accepted => Status == WorldBasicAttackStatus.Accepted;
}

public enum WorldBasicAttackStatus
{
    Accepted,
    MissingAttacker,
    InvalidSkill,
    SequenceReplay,
    AttackerDead,
    AttackerStunned,
    TargetNotFound,
    TargetDead,
    DifferentMap,
    OutOfRange,
    Collision,
    Cooldown
}

public sealed record WorldShieldBashResult(
    WorldShieldBashStatus Status,
    string Message,
    ulong AckSequence,
    ErrorCode ErrorCode,
    CombatEvent? CombatEvent,
    SkillStateChanged? SkillStateChanged,
    CharacterProgressed? CharacterProgressed,
    RewardGranted? RewardGranted = null,
    InventoryDelta? InventoryDelta = null,
    CharacterProgressed? RewardProgressed = null)
{
    public bool Accepted => Status == WorldShieldBashStatus.Accepted;
}

public enum WorldShieldBashStatus
{
    Accepted,
    MissingAttacker,
    InvalidClass,
    InvalidSkill,
    InvalidActionId,
    DuplicateActionId,
    SequenceReplay,
    AttackerDead,
    AttackerStunned,
    TargetNotFound,
    TargetDead,
    DifferentMap,
    OutOfRange,
    Collision,
    Cooldown,
    InsufficientMp
}

public sealed record WorldCombatResourceState(
    string CharacterId,
    int Mp,
    int MaxMp);

public sealed record WorldCharacterSkillState(
    string CharacterId,
    string SkillId,
    int SkillXp,
    int Rank,
    int MaxRank);
