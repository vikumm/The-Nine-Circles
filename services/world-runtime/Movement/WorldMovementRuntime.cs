using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Movement;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Map;

namespace Divinity.WorldRuntime.Movement;

public sealed class WorldMovementRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorldActorState> _actors = new(StringComparer.Ordinal);
    private readonly ICharacterStore _characterStore;
    private readonly WorldMapCatalog _mapCatalog;
    private readonly TimeProvider _timeProvider;
    private ulong _nextSnapshotId = 1;

    public WorldMovementRuntime(ICharacterStore characterStore, WorldMapCatalog mapCatalog, TimeProvider? timeProvider = null)
    {
        _characterStore = characterStore;
        _mapCatalog = mapCatalog;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public WorldJoinMapValidationResult ValidateJoin(WorldJoinMapRequest request) =>
        new WorldJoinMapValidator(_mapCatalog).Validate(request);

    public async Task<WorldJoinResult> JoinAsync(CharacterRecord character, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var checkpoint = await _characterStore.GetCheckpointAsync(character.CharacterId, cancellationToken);
        var spawn = ResolveSpawn(character, checkpoint);
        var stats = ToCharacterStats(character.Stats);
        WorldActorState actor;

        lock (_gate)
        {
            actor = new WorldActorState(
                character.CharacterId,
                _mapCatalog.Map.MapId,
                character.ChannelId,
                _mapCatalog.ContentHash,
                character.Stats,
                spawn.X,
                spawn.Y,
                CardinalDirection.South,
                now,
                now + WorldMovementDefaults.CheckpointInterval);

            actor.MarkSnapshotPublished(now);
            _actors[character.CharacterId] = actor;
        }

        return new WorldJoinResult(
            character.CharacterId,
            actor.MapId,
            actor.ChannelId,
            actor.ContentHash,
            actor.Position,
            stats,
            CreateSnapshot(actor, ackSequence: 0));
    }

    public async Task<WorldMovementResult> ApplyMoveAsync(
        string characterId,
        ulong sequence,
        ulong clientTick,
        MoveIntent intent,
        CancellationToken cancellationToken)
    {
        WorldActorState? actor;
        lock (_gate)
        {
            _actors.TryGetValue(characterId, out actor);
        }

        if (actor is null)
        {
            return Rejected(
                WorldMovementStatus.MissingActor,
                "JoinWorld is required before MoveIntent.",
                characterId,
                sequence,
                ErrorCode.SessionNotJoined);
        }

        WorldMovementResult result;
        CharacterCheckpointRecord? checkpoint = null;

        lock (_gate)
        {
            if (sequence <= actor.LastAcceptedSequence)
            {
                result = Rejected(
                    WorldMovementStatus.SequenceReplay,
                    "MoveIntent sequence is repeated or older than the last accepted sequence.",
                    actor,
                    sequence,
                    ErrorCode.MoveRejected,
                    CorrectionReason.SequenceReplay);
            }
            else if (actor.MotionState is WorldCharacterMotionState.Dead or WorldCharacterMotionState.Stunned)
            {
                actor.LastAcceptedSequence = sequence;
                result = Rejected(
                    WorldMovementStatus.MovementBlockedByState,
                    $"MoveIntent rejected because character is {actor.MotionState}.",
                    actor,
                    sequence,
                    ErrorCode.MoveRejected,
                    CorrectionReason.ProtocolRejected);
            }
            else
            {
                result = ApplyAcceptedSequence(actor, sequence, clientTick, intent, out checkpoint);
            }
        }

        if (checkpoint is not null)
        {
            await _characterStore.StoreCheckpointAsync(checkpoint, cancellationToken);
        }

        return result;
    }

    public void SetCharacterMotionState(string characterId, WorldCharacterMotionState motionState)
    {
        lock (_gate)
        {
            if (_actors.TryGetValue(characterId, out var actor))
            {
                actor.MotionState = motionState;
            }
        }
    }

    public WorldCombatActor? GetCombatActor(string characterId)
    {
        lock (_gate)
        {
            return _actors.TryGetValue(characterId, out var actor)
                ? new WorldCombatActor(
                    actor.CharacterId,
                    actor.MapId,
                    actor.ChannelId,
                    actor.Position,
                    actor.Stats,
                    actor.MotionState.ToCombatActorState())
                : null;
        }
    }

    public bool SegmentTouchesBlockedOrOutOfBounds(CharacterPosition from, double targetX, double targetY) =>
        _mapCatalog.SegmentTouchesBlockedOrOutOfBounds((double)from.X, (double)from.Y, targetX, targetY);

    public async Task DisconnectAsync(string characterId, string reason, CancellationToken cancellationToken)
    {
        CharacterCheckpointRecord? checkpoint = null;

        lock (_gate)
        {
            if (_actors.Remove(characterId, out var actor))
            {
                checkpoint = CreateCheckpoint(actor, reason, _timeProvider.GetUtcNow());
            }
        }

        if (checkpoint is not null)
        {
            await _characterStore.StoreCheckpointAsync(checkpoint, cancellationToken);
        }
    }

    public async Task SaveAllCheckpointsAsync(string reason, CancellationToken cancellationToken)
    {
        CharacterCheckpointRecord[] checkpoints;
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            checkpoints = _actors.Values.Select(actor => CreateCheckpoint(actor, reason, now)).ToArray();
        }

        foreach (var checkpoint in checkpoints)
        {
            await _characterStore.StoreCheckpointAsync(checkpoint, cancellationToken);
        }
    }

    private WorldMovementResult ApplyAcceptedSequence(
        WorldActorState actor,
        ulong sequence,
        ulong clientTick,
        MoveIntent intent,
        out CharacterCheckpointRecord? checkpoint)
    {
        checkpoint = null;
        actor.LastAcceptedSequence = sequence;
        actor.LastClientTick = clientTick;

        var move = ResolveMove(actor, intent);
        if (!move.Success)
        {
            return Rejected(move.Status, move.Message, actor, sequence, ErrorCode.MoveRejected, move.CorrectionReason);
        }

        if (move.Distance <= 0)
        {
            return new WorldMovementResult(
                WorldMovementStatus.Noop,
                "MoveIntent contained no movement.",
                actor.Position,
                actor.Facing,
                sequence,
                null,
                null,
                ErrorCode.Unspecified,
                CheckpointStored: false);
        }

        actor.PositionX = move.X;
        actor.PositionY = move.Y;
        actor.Facing = move.Facing;

        var now = _timeProvider.GetUtcNow();
        var shouldPublishSnapshot = now - actor.LastSnapshotAtUtc >= WorldMovementDefaults.SnapshotInterval;
        var snapshot = shouldPublishSnapshot ? CreateSnapshot(actor, sequence) : null;
        if (snapshot is not null)
        {
            actor.MarkSnapshotPublished(now);
        }

        var checkpointStored = false;
        if (now >= actor.NextCheckpointAtUtc)
        {
            checkpoint = CreateCheckpoint(actor, "periodic", now);
            actor.NextCheckpointAtUtc = now + WorldMovementDefaults.CheckpointInterval;
            checkpointStored = true;
        }

        return new WorldMovementResult(
            WorldMovementStatus.Accepted,
            "MoveIntent accepted.",
            actor.Position,
            actor.Facing,
            sequence,
            snapshot,
            null,
            ErrorCode.Unspecified,
            checkpointStored);
    }

    private ResolvedMove ResolveMove(WorldActorState actor, MoveIntent intent)
    {
        return intent.Mode switch
        {
            MovementMode.Direction => ResolveDirectionMove(actor, intent),
            MovementMode.ClickTarget => ResolveClickMove(actor, intent),
            _ => ResolvedMove.Failed(WorldMovementStatus.InvalidMode, CorrectionReason.ProtocolRejected, "MoveIntent mode is required.")
        };
    }

    private ResolvedMove ResolveDirectionMove(WorldActorState actor, MoveIntent intent)
    {
        var normalized = MovementMath.NormalizeDirection(intent.DirectionX, intent.DirectionY);
        if (!normalized.HasMagnitude)
        {
            return ResolvedMove.Noop(actor);
        }

        var targetX = actor.PositionX + normalized.X * WorldMovementDefaults.MaxDistancePerMoveIntent;
        var targetY = actor.PositionY + normalized.Y * WorldMovementDefaults.MaxDistancePerMoveIntent;
        var facing = MovementMath.ResolveFacing(normalized.X, normalized.Y, actor.Facing);

        return ValidateStep(actor, targetX, targetY, facing);
    }

    private ResolvedMove ResolveClickMove(WorldActorState actor, MoveIntent intent)
    {
        if (!_mapCatalog.IsInsideBounds(intent.TargetX, intent.TargetY))
        {
            return ResolvedMove.Failed(WorldMovementStatus.TargetOutOfBounds, CorrectionReason.ProtocolRejected, "Click-to-move target is outside map bounds.");
        }

        if (_mapCatalog.IsBlocked(intent.TargetX, intent.TargetY))
        {
            return ResolvedMove.Failed(WorldMovementStatus.BlockedDestination, CorrectionReason.Collision, "Click-to-move target is a blocked cell.");
        }

        if (_mapCatalog.SegmentTouchesBlockedOrOutOfBounds(actor.PositionX, actor.PositionY, intent.TargetX, intent.TargetY))
        {
            return ResolvedMove.Failed(WorldMovementStatus.Collision, CorrectionReason.Collision, "Click-to-move direct path crosses blocked or out-of-bounds space.");
        }

        var deltaX = intent.TargetX - actor.PositionX;
        var deltaY = intent.TargetY - actor.PositionY;
        var normalized = MovementMath.NormalizeDirection(deltaX, deltaY);
        if (!normalized.HasMagnitude)
        {
            return ResolvedMove.Noop(actor);
        }

        var distance = Math.Min(WorldMovementDefaults.MaxDistancePerMoveIntent, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
        var targetX = actor.PositionX + normalized.X * distance;
        var targetY = actor.PositionY + normalized.Y * distance;
        var facing = MovementMath.ResolveFacing(normalized.X, normalized.Y, actor.Facing);

        return ValidateStep(actor, targetX, targetY, facing);
    }

    private ResolvedMove ValidateStep(WorldActorState actor, double targetX, double targetY, CardinalDirection facing)
    {
        if (!_mapCatalog.IsInsideBounds(targetX, targetY))
        {
            return ResolvedMove.Failed(WorldMovementStatus.TargetOutOfBounds, CorrectionReason.SpeedLimit, "MoveIntent would leave map bounds.");
        }

        if (_mapCatalog.SegmentTouchesBlockedOrOutOfBounds(actor.PositionX, actor.PositionY, targetX, targetY))
        {
            return ResolvedMove.Failed(WorldMovementStatus.Collision, CorrectionReason.Collision, "MoveIntent would cross blocked or out-of-bounds space.");
        }

        var distance = Math.Sqrt(Math.Pow(targetX - actor.PositionX, 2) + Math.Pow(targetY - actor.PositionY, 2));
        if (distance - WorldMovementDefaults.MaxDistancePerMoveIntent > 0.000001d)
        {
            return ResolvedMove.Failed(WorldMovementStatus.Collision, CorrectionReason.SpeedLimit, "MoveIntent exceeds maximum distance per server tick.");
        }

        return ResolvedMove.Accepted(targetX, targetY, distance, facing);
    }

    private CharacterPosition ResolveSpawn(CharacterRecord character, CharacterCheckpointRecord? checkpoint)
    {
        if (checkpoint is not null
            && string.Equals(checkpoint.MapId, _mapCatalog.Map.MapId, StringComparison.Ordinal)
            && _mapCatalog.IsNavigable((double)checkpoint.Position.X, (double)checkpoint.Position.Y))
        {
            return checkpoint.Position;
        }

        var safeSpawn = _mapCatalog.PrimarySafeSpawn;
        if (safeSpawn is not null && _mapCatalog.IsNavigable(safeSpawn.X, safeSpawn.Y))
        {
            return new CharacterPosition(safeSpawn.X, safeSpawn.Y);
        }

        return character.SafeSpawn;
    }

    private WorldSnapshot CreateSnapshot(WorldActorState actor, ulong ackSequence)
    {
        var snapshotId = _nextSnapshotId++;
        return new WorldSnapshot
        {
            SnapshotId = snapshotId,
            MapId = actor.MapId,
            Entities =
            {
                new EntityState
                {
                    EntityId = actor.CharacterId,
                    Kind = EntityKind.Player,
                    Position = ToVector2(actor.Position),
                    Level = (uint)actor.Stats.Level,
                    Hp = actor.Stats.MaxHp,
                    Mp = actor.Stats.MaxMp
                }
            }
        };
    }

    private static CharacterCheckpointRecord CreateCheckpoint(WorldActorState actor, string reason, DateTimeOffset nowUtc) =>
        new(
            actor.CharacterId,
            actor.MapId,
            actor.Position,
            actor.LastAcceptedSequence,
            reason,
            nowUtc);

    private static WorldMovementResult Rejected(
        WorldMovementStatus status,
        string message,
        string characterId,
        ulong sequence,
        ErrorCode errorCode) =>
        new(
            status,
            message,
            new CharacterPosition(0, 0),
            CardinalDirection.South,
            sequence,
            null,
            null,
            errorCode,
            CheckpointStored: false);

    private static WorldMovementResult Rejected(
        WorldMovementStatus status,
        string message,
        WorldActorState actor,
        ulong sequence,
        ErrorCode errorCode,
        CorrectionReason correctionReason) =>
        new(
            status,
            message,
            actor.Position,
            actor.Facing,
            sequence,
            null,
            new Correction
            {
                EntityId = actor.CharacterId,
                AuthoritativePosition = ToVector2(actor.Position),
                Reason = correctionReason
            },
            errorCode,
            CheckpointStored: false);

    private static CharacterStats ToCharacterStats(KnightStats stats) =>
        new()
        {
            Level = (uint)stats.Level,
            Hp = stats.MaxHp,
            Mp = stats.MaxMp
        };

    private static Vector2 ToVector2(CharacterPosition position) =>
        new()
        {
            X = (float)position.X,
            Y = (float)position.Y
        };

    private sealed class WorldActorState
    {
        public WorldActorState(
            string characterId,
            string mapId,
            string channelId,
            string contentHash,
            KnightStats stats,
            decimal positionX,
            decimal positionY,
            CardinalDirection facing,
            DateTimeOffset lastSnapshotAtUtc,
            DateTimeOffset nextCheckpointAtUtc)
        {
            CharacterId = characterId;
            MapId = mapId;
            ChannelId = channelId;
            ContentHash = contentHash;
            Stats = stats;
            PositionX = (double)positionX;
            PositionY = (double)positionY;
            Facing = facing;
            LastSnapshotAtUtc = lastSnapshotAtUtc;
            NextCheckpointAtUtc = nextCheckpointAtUtc;
        }

        public string CharacterId { get; }
        public string MapId { get; }
        public string ChannelId { get; }
        public string ContentHash { get; }
        public KnightStats Stats { get; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public CardinalDirection Facing { get; set; }
        public ulong LastAcceptedSequence { get; set; }
        public ulong LastClientTick { get; set; }
        public WorldCharacterMotionState MotionState { get; set; } = WorldCharacterMotionState.Alive;
        public DateTimeOffset LastSnapshotAtUtc { get; private set; }
        public DateTimeOffset NextCheckpointAtUtc { get; set; }
        public CharacterPosition Position => new((decimal)PositionX, (decimal)PositionY);

        public void MarkSnapshotPublished(DateTimeOffset nowUtc) => LastSnapshotAtUtc = nowUtc;
    }

    private readonly record struct ResolvedMove(
        bool Success,
        WorldMovementStatus Status,
        CorrectionReason CorrectionReason,
        string Message,
        double X,
        double Y,
        double Distance,
        CardinalDirection Facing)
    {
        public static ResolvedMove Accepted(double x, double y, double distance, CardinalDirection facing) =>
            new(true, WorldMovementStatus.Accepted, CorrectionReason.Unspecified, "MoveIntent accepted.", x, y, distance, facing);

        public static ResolvedMove Noop(WorldActorState actor) =>
            new(true, WorldMovementStatus.Noop, CorrectionReason.Unspecified, "MoveIntent contained no movement.", actor.PositionX, actor.PositionY, 0, actor.Facing);

        public static ResolvedMove Failed(WorldMovementStatus status, CorrectionReason reason, string message) =>
            new(false, status, reason, message, 0, 0, 0, CardinalDirection.South);
    }
}
