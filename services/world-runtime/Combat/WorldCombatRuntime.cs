using Divinity.Contracts.V1;
using Divinity.GameRules.Combat;
using Divinity.GameRules.Monsters;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;

namespace Divinity.WorldRuntime.Combat;

public sealed class WorldCombatRuntime
{
    private readonly object _gate = new();
    private readonly WorldMovementRuntime _movementRuntime;
    private readonly WorldMonsterRuntime _monsterRuntime;
    private readonly TimeProvider _timeProvider;
    private readonly ICombatRandomSource _random;
    private readonly Dictionary<string, ulong> _lastAcceptedSequenceByAttacker = new(StringComparer.Ordinal);
    private readonly Dictionary<(string CharacterId, string SkillId), DateTimeOffset> _cooldownEndsByCharacterSkill = new();
    private ulong _nextCombatEventId = 1;

    public WorldCombatRuntime(
        WorldMovementRuntime movementRuntime,
        WorldMonsterRuntime monsterRuntime,
        TimeProvider? timeProvider = null,
        ICombatRandomSource? random = null)
    {
        _movementRuntime = movementRuntime;
        _monsterRuntime = monsterRuntime;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _random = random ?? new SystemCombatRandomSource();
    }

    public WorldBasicAttackResult ApplyBasicAttack(string attackerCharacterId, ulong sequence, AttackIntent intent)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            if (!string.Equals(intent.SkillId, BasicSlashCatalog.SkillId, StringComparison.Ordinal))
            {
                return Rejected(WorldBasicAttackStatus.InvalidSkill, sequence, "AttackIntent skill_id must be knight_basic_slash.");
            }

            if (_lastAcceptedSequenceByAttacker.TryGetValue(attackerCharacterId, out var lastSequence)
                && sequence <= lastSequence)
            {
                return Rejected(WorldBasicAttackStatus.SequenceReplay, sequence, "AttackIntent sequence is repeated or older than the last accepted attack sequence.");
            }

            var attacker = _movementRuntime.GetCombatActor(attackerCharacterId);
            if (attacker is null)
            {
                return Rejected(WorldBasicAttackStatus.MissingAttacker, sequence, "JoinWorld is required before AttackIntent.");
            }

            if (attacker.State == WorldCombatActorState.Dead)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.AttackerDead, sequence, "Dead characters cannot attack.");
            }

            if (attacker.State == WorldCombatActorState.Stunned)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.AttackerStunned, sequence, "Stunned characters cannot attack.");
            }

            var target = _monsterRuntime.GetCombatTarget(intent.TargetEntityId);
            if (target is null)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.TargetNotFound, sequence, "AttackIntent target does not exist.");
            }

            if (!target.Active || target.Hp <= 0)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.TargetDead, sequence, "AttackIntent target is dead.");
            }

            if (!string.Equals(attacker.MapId, target.MapId, StringComparison.Ordinal)
                || !string.Equals(attacker.ChannelId, target.ChannelId, StringComparison.Ordinal))
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.DifferentMap, sequence, "Attacker and target must be in the same map and channel.");
            }

            var cooldownKey = (attackerCharacterId, BasicSlashCatalog.SkillId);
            if (_cooldownEndsByCharacterSkill.TryGetValue(cooldownKey, out var cooldownEndsAt) && now < cooldownEndsAt)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(
                    WorldBasicAttackStatus.Cooldown,
                    sequence,
                    "knight_basic_slash is still on server-side cooldown.",
                    CreateSkillState(attackerCharacterId, now, cooldownEndsAt, available: false));
            }

            var distance = Distance((double)attacker.Position.X, (double)attacker.Position.Y, target.X, target.Y);
            if (distance > BasicSlashCatalog.RangeUnits)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.OutOfRange, sequence, "AttackIntent target is outside knight_basic_slash range.");
            }

            if (_movementRuntime.SegmentTouchesBlockedOrOutOfBounds(attacker.Position, target.X, target.Y))
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.Collision, sequence, "AttackIntent line to target is blocked.");
            }

            var roll = BasicSlashDamageCalculator.Calculate(attacker.Stats, target.Defense, _random);
            var damage = _monsterRuntime.ApplyDamageToMonster(target.EntityId, roll.Damage, attackerCharacterId);
            if (!damage.Success)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return Rejected(WorldBasicAttackStatus.TargetDead, sequence, damage.Message);
            }

            var cooldownEnd = now + BasicSlashCatalog.Cooldown;
            _cooldownEndsByCharacterSkill[cooldownKey] = cooldownEnd;
            _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;

            return new WorldBasicAttackResult(
                WorldBasicAttackStatus.Accepted,
                "AttackIntent accepted.",
                sequence,
                ErrorCode.Unspecified,
                new CombatEvent
                {
                    EventId = $"combat:{_nextCombatEventId++}",
                    SourceEntityId = attackerCharacterId,
                    TargetEntityId = target.EntityId,
                    SkillId = BasicSlashCatalog.SkillId,
                    Result = roll.Critical ? CombatResult.Critical : CombatResult.Hit,
                    Damage = damage.DamageApplied,
                    TargetHp = damage.TargetHp
                },
                CreateSkillState(attackerCharacterId, now, cooldownEnd, available: false));
        }
    }

    private static WorldBasicAttackResult Rejected(
        WorldBasicAttackStatus status,
        ulong sequence,
        string message,
        SkillStateChanged? skillStateChanged = null) =>
        new(status, message, sequence, ErrorCode.AttackRejected, null, skillStateChanged);

    private static SkillStateChanged CreateSkillState(string characterId, DateTimeOffset now, DateTimeOffset cooldownEndsAt, bool available) =>
        new()
        {
            CharacterId = characterId,
            SkillId = BasicSlashCatalog.SkillId,
            CooldownStartedServerMs = ToUnixMilliseconds(now),
            CooldownEndsServerMs = ToUnixMilliseconds(cooldownEndsAt),
            Available = available
        };

    private static ulong ToUnixMilliseconds(DateTimeOffset value) =>
        (ulong)value.ToUnixTimeMilliseconds();

    private static double Distance(double startX, double startY, double endX, double endY)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}
