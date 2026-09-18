using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Movement;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Equipment;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Observability;

namespace Divinity.WorldRuntime.Movement;

public sealed class WorldMovementRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorldActorState> _actors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldPendingDeathState> _pendingDeaths = new(StringComparer.Ordinal);
    private readonly ICharacterStore _characterStore;
    private readonly WorldEquipmentRuntime _equipmentRuntime;
    private readonly WorldMapCatalog _mapCatalog;
    private readonly TimeProvider _timeProvider;
    private ulong _nextSnapshotId = 1;
    private ulong _nextCombatEventId = 1;

    public WorldMovementRuntime(
        ICharacterStore characterStore,
        WorldMapCatalog mapCatalog,
        TimeProvider? timeProvider = null,
        WorldEquipmentRuntime? equipmentRuntime = null)
    {
        _characterStore = characterStore;
        _equipmentRuntime = equipmentRuntime ?? new WorldEquipmentRuntime();
        _mapCatalog = mapCatalog;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public WorldJoinMapValidationResult ValidateJoin(WorldJoinMapRequest request) =>
        new WorldJoinMapValidator(_mapCatalog).Validate(request);

    public int ActiveActorCount
    {
        get
        {
            lock (_gate)
            {
                return _actors.Count;
            }
        }
    }

    public async Task<WorldJoinResult> JoinAsync(CharacterRecord character, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var checkpoint = await _characterStore.GetCheckpointAsync(character.CharacterId, cancellationToken);
        var spawn = ResolveSpawn(character, checkpoint);
        CharacterStats stats;
        CharacterCheckpointRecord? respawnCheckpoint = null;
        WorldActorState actor;

        lock (_gate)
        {
            if (_actors.TryGetValue(character.CharacterId, out var existingActor))
            {
                actor = existingActor;
                actor.DisconnectedAtUtc = null;
                actor.DisconnectGraceExpiresAtUtc = null;
                actor.MarkSnapshotPublished(now);
                stats = ToCharacterStats(actor);
            }
            else
            {
                _pendingDeaths.Remove(character.CharacterId, out var pendingDeath);
                if (pendingDeath is not null)
                {
                    spawn = pendingDeath.Position;
                }

                actor = new WorldActorState(
                    character.CharacterId,
                    _mapCatalog.Map.MapId,
                    character.ChannelId,
                    _mapCatalog.ContentHash,
                    character.Vocation,
                    character.Stats,
                    spawn.X,
                    spawn.Y,
                    CardinalDirection.South,
                    now,
                    now + WorldMovementDefaults.CheckpointInterval);
                if (checkpoint?.CurrentHp is not null)
                {
                    actor.CurrentHp = Math.Max(0, Math.Min(checkpoint.CurrentHp.Value, actor.Stats.MaxHp));
                }

                if (checkpoint?.CurrentMp is not null)
                {
                    actor.CurrentMp = Math.Max(0, Math.Min(checkpoint.CurrentMp.Value, actor.Stats.MaxMp));
                }

                if (pendingDeath is not null)
                {
                    actor.CurrentHp = 0;
                    actor.CurrentMp = pendingDeath.CurrentMp;
                    actor.MotionState = WorldCharacterMotionState.Dead;
                    actor.DeathStartedAtUtc = pendingDeath.DeathStartedAtUtc;
                    actor.RespawnAtUtc = pendingDeath.RespawnAtUtc;

                    if (now >= pendingDeath.RespawnAtUtc)
                    {
                        RespawnActor(actor, now);
                        respawnCheckpoint = CreateCheckpoint(actor, "respawn", now);
                    }
                }

                actor.MarkSnapshotPublished(now);
                _actors[character.CharacterId] = actor;
                stats = ToCharacterStats(actor);
            }
        }

        if (respawnCheckpoint is not null)
        {
            await _characterStore.StoreCheckpointAsync(respawnCheckpoint, cancellationToken);
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
                if (motionState == WorldCharacterMotionState.Dead)
                {
                    actor.CurrentHp = 0;
                }
            }
        }
    }

    public void MarkCombatActivity(string characterId)
    {
        lock (_gate)
        {
            if (_actors.TryGetValue(characterId, out var actor))
            {
                actor.LastCombatAtUtc = _timeProvider.GetUtcNow();
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
                    actor.Vocation,
                    actor.Stats with { Defense = actor.Stats.Defense + _equipmentRuntime.GetDefenseBonus(actor.CharacterId) },
                    actor.MotionState.ToCombatActorState())
                : null;
        }
    }

    public WorldCharacterDamageResult ApplyDamageToCharacter(
        string characterId,
        int damage,
        string sourceEntityId,
        string skillId)
    {
        lock (_gate)
        {
            if (!_actors.TryGetValue(characterId, out var actor))
            {
                return new WorldCharacterDamageResult(
                    WorldCharacterDamageStatus.MissingActor,
                    "Character actor is not joined.",
                    characterId,
                    DamageApplied: 0,
                    TargetHp: 0,
                    Snapshot: null,
                    CombatEvent: null,
                    InventoryDelta: null,
                    RespawnAtUtc: null);
            }

            if (actor.MotionState == WorldCharacterMotionState.Dead || actor.CurrentHp <= 0)
            {
                return new WorldCharacterDamageResult(
                    WorldCharacterDamageStatus.AlreadyDead,
                    "Character is already dead.",
                    characterId,
                    DamageApplied: 0,
                    TargetHp: actor.CurrentHp,
                    Snapshot: null,
                    CombatEvent: null,
                    InventoryDelta: null,
                    RespawnAtUtc: actor.RespawnAtUtc);
            }

            var now = _timeProvider.GetUtcNow();
            actor.LastCombatAtUtc = now;
            var damageApplied = Math.Max(1, damage);
            actor.CurrentHp = Math.Max(0, actor.CurrentHp - damageApplied);
            if (actor.CurrentHp > 0)
            {
                return new WorldCharacterDamageResult(
                    WorldCharacterDamageStatus.Damaged,
                    "Character damaged.",
                    characterId,
                    damageApplied,
                    actor.CurrentHp,
                    CreateSnapshot(actor, actor.LastAcceptedSequence),
                    CreateCharacterCombatEvent(actor, sourceEntityId, skillId, damageApplied, killed: false),
                    InventoryDelta: null,
                    RespawnAtUtc: null);
            }

            actor.MotionState = WorldCharacterMotionState.Dead;
            actor.DeathStartedAtUtc = now;
            actor.RespawnAtUtc = now + CharacterLifeCatalog.DeathScreenDuration;
            var durability = _equipmentRuntime.ApplyDeathDurabilityLoss(characterId);

            return new WorldCharacterDamageResult(
                WorldCharacterDamageStatus.Killed,
                "Character killed; respawn pending at Safe Spawn.",
                characterId,
                damageApplied,
                actor.CurrentHp,
                CreateSnapshot(actor, actor.LastAcceptedSequence),
                CreateCharacterCombatEvent(actor, sourceEntityId, skillId, damageApplied, killed: true),
                durability.InventoryDelta,
                actor.RespawnAtUtc);
        }
    }

    public async Task<IReadOnlyList<WorldCharacterRespawnResult>> RespawnDueCharactersAsync(CancellationToken cancellationToken)
    {
        List<CharacterCheckpointRecord> checkpoints = [];
        List<WorldCharacterRespawnResult> results = [];

        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var actor in _actors.Values)
            {
                if (actor.MotionState != WorldCharacterMotionState.Dead
                    || actor.RespawnAtUtc is null
                    || now < actor.RespawnAtUtc)
                {
                    continue;
                }

                RespawnActor(actor, now);
                var checkpoint = CreateCheckpoint(actor, "respawn", now);
                checkpoints.Add(checkpoint);
                results.Add(new WorldCharacterRespawnResult(
                    actor.CharacterId,
                    actor.Position,
                    ToCharacterStats(actor.Stats),
                    CreateSnapshot(actor, actor.LastAcceptedSequence),
                    CheckpointStored: true));
            }

            foreach (var pending in _pendingDeaths.Values
                .Where(pending => now >= pending.RespawnAtUtc)
                .ToArray())
            {
                var checkpoint = new CharacterCheckpointRecord(
                    pending.CharacterId,
                    pending.MapId,
                    ResolveSafeSpawn(),
                    pending.LastAcceptedSequence,
                    "respawn",
                    now);
                checkpoints.Add(checkpoint);
                _pendingDeaths.Remove(pending.CharacterId);
            }
        }

        foreach (var checkpoint in checkpoints)
        {
            await _characterStore.StoreCheckpointAsync(checkpoint, cancellationToken);
        }

        return results;
    }

    public bool SegmentTouchesBlockedOrOutOfBounds(CharacterPosition from, double targetX, double targetY) =>
        _mapCatalog.SegmentTouchesBlockedOrOutOfBounds((double)from.X, (double)from.Y, targetX, targetY);

    public async Task<WorldCharacterDisconnectResult> DisconnectAsync(string characterId, string reason, CancellationToken cancellationToken)
    {
        CharacterCheckpointRecord? checkpoint = null;
        WorldCharacterDisconnectResult result;

        lock (_gate)
        {
            if (_actors.Remove(characterId, out var actor))
            {
                var now = _timeProvider.GetUtcNow();
                checkpoint = CreateCheckpoint(actor, reason, now);
                var retainForCombatGrace = IsAbruptDisconnect(reason) && IsInCombatGrace(actor, now);
                if (retainForCombatGrace)
                {
                    actor.DisconnectedAtUtc = now;
                    actor.DisconnectGraceExpiresAtUtc = now + WorldMovementDefaults.CombatDisconnectGrace;
                    _actors[characterId] = actor;
                    result = new WorldCharacterDisconnectResult(
                        characterId,
                        ActorRetained: true,
                        CheckpointStored: true,
                        actor.DisconnectGraceExpiresAtUtc,
                        reason);
                }
                else if (actor.MotionState == WorldCharacterMotionState.Dead && actor.RespawnAtUtc is not null && actor.DeathStartedAtUtc is not null)
                {
                    _pendingDeaths[characterId] = new WorldPendingDeathState(
                        actor.CharacterId,
                        actor.MapId,
                        actor.Position,
                        actor.CurrentMp,
                        actor.LastAcceptedSequence,
                        actor.DeathStartedAtUtc.Value,
                        actor.RespawnAtUtc.Value);
                    result = new WorldCharacterDisconnectResult(characterId, ActorRetained: false, CheckpointStored: true, null, reason);
                }
                else
                {
                    result = new WorldCharacterDisconnectResult(characterId, ActorRetained: false, CheckpointStored: true, null, reason);
                }
            }
            else
            {
                result = new WorldCharacterDisconnectResult(characterId, ActorRetained: false, CheckpointStored: false, null, reason);
            }
        }

        if (checkpoint is not null)
        {
            await _characterStore.StoreCheckpointAsync(checkpoint, cancellationToken);
        }

        return result;
    }

    public async Task<IReadOnlyList<WorldCharacterDisconnectResult>> ExpireDisconnectGraceAsync(CancellationToken cancellationToken)
    {
        List<CharacterCheckpointRecord> checkpoints = [];
        List<WorldCharacterDisconnectResult> results = [];

        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var actor in _actors.Values
                .Where(actor => actor.DisconnectGraceExpiresAtUtc is not null && actor.DisconnectGraceExpiresAtUtc <= now)
                .ToArray())
            {
                _actors.Remove(actor.CharacterId);
                var checkpoint = CreateCheckpoint(actor, "disconnect_grace_expired", now);
                checkpoints.Add(checkpoint);
                results.Add(new WorldCharacterDisconnectResult(
                    actor.CharacterId,
                    ActorRetained: false,
                    CheckpointStored: true,
                    actor.DisconnectGraceExpiresAtUtc,
                    "disconnect_grace_expired"));
            }
        }

        foreach (var checkpoint in checkpoints)
        {
            await _characterStore.StoreCheckpointAsync(checkpoint, cancellationToken);
        }

        return results;
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

    private CharacterPosition ResolveSafeSpawn()
    {
        var safeSpawn = _mapCatalog.PrimarySafeSpawn;
        return safeSpawn is not null && _mapCatalog.IsNavigable(safeSpawn.X, safeSpawn.Y)
            ? new CharacterPosition(safeSpawn.X, safeSpawn.Y)
            : KnightCatalog.SafeSpawn;
    }

    private void RespawnActor(WorldActorState actor, DateTimeOffset now)
    {
        var safeSpawn = ResolveSafeSpawn();
        actor.PositionX = (double)safeSpawn.X;
        actor.PositionY = (double)safeSpawn.Y;
        actor.Facing = CardinalDirection.South;
        actor.CurrentHp = actor.Stats.MaxHp;
        actor.CurrentMp = actor.Stats.MaxMp;
        actor.MotionState = WorldCharacterMotionState.Alive;
        actor.DeathStartedAtUtc = null;
        actor.RespawnAtUtc = null;
        actor.NextCheckpointAtUtc = now + WorldMovementDefaults.CheckpointInterval;
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
                    Hp = actor.CurrentHp,
                    Mp = actor.CurrentMp
                }
            }
        };
    }

    private CombatEvent CreateCharacterCombatEvent(
        WorldActorState actor,
        string sourceEntityId,
        string skillId,
        int damageApplied,
        bool killed)
    {
        var combatEvent = new CombatEvent
        {
            EventId = $"combat:{_nextCombatEventId++}",
            SourceEntityId = sourceEntityId,
            TargetEntityId = actor.CharacterId,
            SkillId = skillId,
            Result = CombatResult.Hit,
            Damage = damageApplied,
            TargetHp = actor.CurrentHp
        };

        if (killed)
        {
            combatEvent.StatusEffects.Add(new StatusEffectApplied
            {
                EffectId = CharacterLifeCatalog.DeathEffectId,
                DurationMs = (uint)CharacterLifeCatalog.DeathScreenDuration.TotalMilliseconds
            });
        }

        return combatEvent;
    }

    private static CharacterCheckpointRecord CreateCheckpoint(WorldActorState actor, string reason, DateTimeOffset nowUtc) =>
        new(
            actor.CharacterId,
            actor.MapId,
            actor.Position,
            actor.LastAcceptedSequence,
            reason,
            nowUtc,
            actor.CurrentHp,
            actor.CurrentMp,
            actor.Stats.Level);

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
        CorrectionReason correctionReason)
    {
        WorldRuntimeTelemetry.RecordMovementCorrection(correctionReason.ToString());
        return new WorldMovementResult(
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
    }

    private static CharacterStats ToCharacterStats(KnightStats stats) =>
        new()
        {
            Level = (uint)stats.Level,
            Hp = stats.MaxHp,
            Mp = stats.MaxMp
        };

    private static CharacterStats ToCharacterStats(WorldActorState actor) =>
        new()
        {
            Level = (uint)actor.Stats.Level,
            Hp = actor.CurrentHp,
            Mp = actor.CurrentMp
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
            string vocation,
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
            Vocation = vocation;
            Stats = stats;
            PositionX = (double)positionX;
            PositionY = (double)positionY;
            Facing = facing;
            CurrentHp = stats.MaxHp;
            CurrentMp = stats.MaxMp;
            LastSnapshotAtUtc = lastSnapshotAtUtc;
            NextCheckpointAtUtc = nextCheckpointAtUtc;
        }

        public string CharacterId { get; }
        public string MapId { get; }
        public string ChannelId { get; }
        public string ContentHash { get; }
        public string Vocation { get; }
        public KnightStats Stats { get; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public CardinalDirection Facing { get; set; }
        public int CurrentHp { get; set; }
        public int CurrentMp { get; set; }
        public ulong LastAcceptedSequence { get; set; }
        public ulong LastClientTick { get; set; }
        public WorldCharacterMotionState MotionState { get; set; } = WorldCharacterMotionState.Alive;
        public DateTimeOffset? DeathStartedAtUtc { get; set; }
        public DateTimeOffset? RespawnAtUtc { get; set; }
        public DateTimeOffset? LastCombatAtUtc { get; set; }
        public DateTimeOffset? DisconnectedAtUtc { get; set; }
        public DateTimeOffset? DisconnectGraceExpiresAtUtc { get; set; }
        public DateTimeOffset LastSnapshotAtUtc { get; private set; }
        public DateTimeOffset NextCheckpointAtUtc { get; set; }
        public CharacterPosition Position => new((decimal)PositionX, (decimal)PositionY);

        public void MarkSnapshotPublished(DateTimeOffset nowUtc) => LastSnapshotAtUtc = nowUtc;
    }

    private static bool IsAbruptDisconnect(string reason) =>
        reason.Contains("abrupt", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("transport", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private static bool IsInCombatGrace(WorldActorState actor, DateTimeOffset now) =>
        actor.LastCombatAtUtc is not null &&
        now - actor.LastCombatAtUtc.Value <= WorldMovementDefaults.CombatDisconnectGrace;

    private sealed record WorldPendingDeathState(
        string CharacterId,
        string MapId,
        CharacterPosition Position,
        int CurrentMp,
        ulong LastAcceptedSequence,
        DateTimeOffset DeathStartedAtUtc,
        DateTimeOffset RespawnAtUtc);

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
