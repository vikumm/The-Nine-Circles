using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Movement;

namespace Divinity.WorldRuntime.Movement;

public static class WorldMovementDefaults
{
    public const double MaxSpeedUnitsPerSecond = 4.5d;
    public const double MoveIntentDeltaSeconds = 0.05d;
    public const double MaxDistancePerMoveIntent = MaxSpeedUnitsPerSecond * MoveIntentDeltaSeconds;
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
