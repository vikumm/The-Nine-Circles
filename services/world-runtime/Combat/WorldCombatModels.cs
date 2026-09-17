using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;

namespace Divinity.WorldRuntime.Combat;

public sealed record WorldCombatActor(
    string EntityId,
    string MapId,
    string ChannelId,
    CharacterPosition Position,
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
    SkillStateChanged? SkillStateChanged)
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
