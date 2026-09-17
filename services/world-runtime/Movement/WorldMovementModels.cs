using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Movement;
using Divinity.WorldRuntime.Combat;

namespace Divinity.WorldRuntime.Movement;

public static class WorldMovementDefaults
{
    public const double MaxSpeedUnitsPerSecond = MovementTuning.MaxSpeedUnitsPerSecond;
    public const double MoveIntentDeltaSeconds = MovementTuning.MoveIntentDeltaSeconds;
    public const double MaxDistancePerMoveIntent = MovementTuning.MaxDistancePerMoveIntent;
    public static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(10);
}

public sealed record WorldJoinResult(
    string CharacterId,
    string MapId,
    string ChannelId,
    string ContentHash,
    CharacterPosition Position,
    CharacterStats Stats,
    WorldSnapshot Snapshot);

public sealed record WorldMovementResult(
    WorldMovementStatus Status,
    string Message,
    CharacterPosition Position,
    CardinalDirection Facing,
    ulong AckSequence,
    WorldSnapshot? Snapshot,
    Correction? Correction,
    ErrorCode ErrorCode,
    bool CheckpointStored)
{
    public bool Accepted => Status is WorldMovementStatus.Accepted or WorldMovementStatus.Noop;
}

public enum WorldMovementStatus
{
    Accepted,
    Noop,
    MissingActor,
    SequenceReplay,
    MovementBlockedByState,
    InvalidMode,
    TargetOutOfBounds,
    BlockedDestination,
    Collision
}

public enum WorldCharacterMotionState
{
    Alive,
    Dead,
    Stunned
}

public static class WorldCharacterMotionStateExtensions
{
    public static WorldCombatActorState ToCombatActorState(this WorldCharacterMotionState motionState) =>
        motionState switch
        {
            WorldCharacterMotionState.Dead => WorldCombatActorState.Dead,
            WorldCharacterMotionState.Stunned => WorldCombatActorState.Stunned,
            _ => WorldCombatActorState.Alive
        };
}
