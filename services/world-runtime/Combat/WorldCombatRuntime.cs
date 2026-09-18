using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Combat;
using Divinity.GameRules.Monsters;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;
using Divinity.WorldRuntime.Observability;
using Divinity.WorldRuntime.Rewards;

namespace Divinity.WorldRuntime.Combat;

public sealed class WorldCombatRuntime
{
    private readonly object _gate = new();
    private readonly WorldMovementRuntime _movementRuntime;
    private readonly WorldMonsterRuntime _monsterRuntime;
    private readonly WorldRewardRuntime? _rewardRuntime;
    private readonly TimeProvider _timeProvider;
    private readonly ICombatRandomSource _random;
    private readonly Dictionary<string, ulong> _lastAcceptedSequenceByAttacker = new(StringComparer.Ordinal);
    private readonly Dictionary<(string CharacterId, string SkillId), DateTimeOffset> _cooldownEndsByCharacterSkill = new();
    private readonly Dictionary<string, WorldCombatResourceState> _resourcesByCharacterId = new(StringComparer.Ordinal);
    private readonly Dictionary<(string CharacterId, string SkillId), WorldCharacterSkillState> _skillStates = new();
    private readonly HashSet<(string CharacterId, string SkillId, string ActionId)> _processedSkillActions = new();
    private ulong _nextCombatEventId = 1;

    public WorldCombatRuntime(
        WorldMovementRuntime movementRuntime,
        WorldMonsterRuntime monsterRuntime,
        TimeProvider? timeProvider = null,
        ICombatRandomSource? random = null,
        WorldRewardRuntime? rewardRuntime = null)
    {
        _movementRuntime = movementRuntime;
        _monsterRuntime = monsterRuntime;
        _rewardRuntime = rewardRuntime;
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
                    CreateSkillState(attackerCharacterId, BasicSlashCatalog.SkillId, now, cooldownEndsAt, available: false));
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
            _movementRuntime.MarkCombatActivity(attackerCharacterId);
            var reward = GrantRewardIfKilled(damage.KillId, attackerCharacterId);
            if (!string.IsNullOrWhiteSpace(damage.KillId))
            {
                WorldRuntimeTelemetry.RecordMonsterDeath(target.TemplateId);
                using var activity = WorldRuntimeTelemetry.StartActivity("divinity.world.kill_reward_inventory_delta", attackerCharacterId, target.MapId, "kill_reward");
                activity?.SetTag("kill_id", damage.KillId);
                activity?.SetTag("reward_key", reward?.RewardKey ?? string.Empty);
                activity?.SetTag("inventory_delta", reward?.InventoryDelta is not null);
            }

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
                    TargetHp = damage.TargetHp,
                    KillId = damage.KillId ?? string.Empty
                },
                CreateSkillState(attackerCharacterId, BasicSlashCatalog.SkillId, now, cooldownEnd, available: false),
                reward?.RewardGranted,
                reward?.InventoryDelta,
                reward?.CharacterProgressed);
        }
    }

    public WorldShieldBashResult ApplyShieldBash(string attackerCharacterId, ulong sequence, CastIntent intent)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            if (!string.Equals(intent.SkillId, ShieldBashCatalog.SkillId, StringComparison.Ordinal))
            {
                return CastRejected(WorldShieldBashStatus.InvalidSkill, sequence, "CastIntent skill_id must be knight_shield_bash_r1.");
            }

            if (string.IsNullOrWhiteSpace(intent.ActionId))
            {
                return CastRejected(WorldShieldBashStatus.InvalidActionId, sequence, "CastIntent action_id is required for Shield Bash idempotency.");
            }

            if (_lastAcceptedSequenceByAttacker.TryGetValue(attackerCharacterId, out var lastSequence)
                && sequence <= lastSequence)
            {
                return CastRejected(WorldShieldBashStatus.SequenceReplay, sequence, "CastIntent sequence is repeated or older than the last accepted combat sequence.");
            }

            var attacker = _movementRuntime.GetCombatActor(attackerCharacterId);
            if (attacker is null)
            {
                return CastRejected(WorldShieldBashStatus.MissingAttacker, sequence, "JoinWorld is required before CastIntent.");
            }

            if (!string.Equals(attacker.Vocation, KnightCatalog.Vocation, StringComparison.Ordinal))
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.InvalidClass, sequence, "Shield Bash is only available to Knight in MMO-VS1.");
            }

            if (attacker.State == WorldCombatActorState.Dead)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.AttackerDead, sequence, "Dead characters cannot cast Shield Bash.");
            }

            if (attacker.State == WorldCombatActorState.Stunned)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.AttackerStunned, sequence, "Stunned characters cannot cast Shield Bash.");
            }

            var actionKey = (attackerCharacterId, ShieldBashCatalog.SkillId, intent.ActionId);
            if (_processedSkillActions.Contains(actionKey))
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.DuplicateActionId, sequence, "Shield Bash action_id has already been processed.");
            }

            var resource = EnsureResource(attacker);
            if (resource.Mp < ShieldBashCatalog.ResourceCostMp)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.InsufficientMp, sequence, "Not enough MP for Shield Bash.");
            }

            var target = _monsterRuntime.GetCombatTarget(intent.TargetEntityId);
            if (target is null)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.TargetNotFound, sequence, "CastIntent target does not exist.");
            }

            if (!target.Active || target.Hp <= 0)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.TargetDead, sequence, "CastIntent target is dead.");
            }

            if (!string.Equals(attacker.MapId, target.MapId, StringComparison.Ordinal)
                || !string.Equals(attacker.ChannelId, target.ChannelId, StringComparison.Ordinal))
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.DifferentMap, sequence, "Attacker and target must be in the same map and channel.");
            }

            var cooldownKey = (attackerCharacterId, ShieldBashCatalog.SkillId);
            if (_cooldownEndsByCharacterSkill.TryGetValue(cooldownKey, out var cooldownEndsAt) && now < cooldownEndsAt)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(
                    WorldShieldBashStatus.Cooldown,
                    sequence,
                    "knight_shield_bash_r1 is still on server-side cooldown.",
                    CreateSkillState(attackerCharacterId, ShieldBashCatalog.SkillId, now, cooldownEndsAt, available: false));
            }

            var distance = Distance((double)attacker.Position.X, (double)attacker.Position.Y, target.X, target.Y);
            if (distance > ShieldBashCatalog.RangeUnits)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.OutOfRange, sequence, "CastIntent target is outside Shield Bash range.");
            }

            if (_movementRuntime.SegmentTouchesBlockedOrOutOfBounds(attacker.Position, target.X, target.Y))
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.Collision, sequence, "CastIntent line to target is blocked.");
            }

            var roll = ShieldBashDamageCalculator.Calculate(attacker.Stats, target.Defense, _random);
            var damage = _monsterRuntime.ApplyDamageToMonster(target.EntityId, roll.Damage, attackerCharacterId);
            if (!damage.Success)
            {
                _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
                return CastRejected(WorldShieldBashStatus.TargetDead, sequence, damage.Message);
            }

            _ = _monsterRuntime.ApplyStunToMonster(target.EntityId, ShieldBashCatalog.StunDuration);
            var cooldownEnd = now + ShieldBashCatalog.Cooldown;
            _cooldownEndsByCharacterSkill[cooldownKey] = cooldownEnd;
            _resourcesByCharacterId[attackerCharacterId] = resource with { Mp = resource.Mp - ShieldBashCatalog.ResourceCostMp };
            _processedSkillActions.Add(actionKey);
            _lastAcceptedSequenceByAttacker[attackerCharacterId] = sequence;
            _movementRuntime.MarkCombatActivity(attackerCharacterId);
            var progressed = AddSkillXp(attackerCharacterId, ShieldBashCatalog.SkillId, ShieldBashCatalog.SkillXpPerValidHit);
            var reward = GrantRewardIfKilled(damage.KillId, attackerCharacterId, ShieldBashCatalog.SkillId, ShieldBashCatalog.SkillXpPerValidHit);
            if (!string.IsNullOrWhiteSpace(damage.KillId))
            {
                WorldRuntimeTelemetry.RecordMonsterDeath(target.TemplateId);
                using var activity = WorldRuntimeTelemetry.StartActivity("divinity.world.kill_reward_inventory_delta", attackerCharacterId, target.MapId, "kill_reward");
                activity?.SetTag("kill_id", damage.KillId);
                activity?.SetTag("reward_key", reward?.RewardKey ?? string.Empty);
                activity?.SetTag("inventory_delta", reward?.InventoryDelta is not null);
            }

            return new WorldShieldBashResult(
                WorldShieldBashStatus.Accepted,
                "CastIntent accepted.",
                sequence,
                ErrorCode.Unspecified,
                new CombatEvent
                {
                    EventId = $"combat:{_nextCombatEventId++}",
                    SourceEntityId = attackerCharacterId,
                    TargetEntityId = target.EntityId,
                    SkillId = ShieldBashCatalog.SkillId,
                    Result = roll.Critical ? CombatResult.Critical : CombatResult.Hit,
                    Damage = damage.DamageApplied,
                    TargetHp = damage.TargetHp,
                    KillId = damage.KillId ?? string.Empty,
                    StatusEffects =
                    {
                        new StatusEffectApplied
                        {
                            EffectId = ShieldBashCatalog.StunEffectId,
                            DurationMs = (uint)ShieldBashCatalog.StunDuration.TotalMilliseconds
                        }
                    }
                },
                CreateSkillState(attackerCharacterId, ShieldBashCatalog.SkillId, now, cooldownEnd, available: false),
                ToCharacterProgressed(progressed),
                reward?.RewardGranted,
                reward?.InventoryDelta,
                reward?.CharacterProgressed);
        }
    }

    public WorldCombatResourceState? GetResourceState(string characterId)
    {
        lock (_gate)
        {
            if (_resourcesByCharacterId.TryGetValue(characterId, out var resource))
            {
                return resource;
            }

            var actor = _movementRuntime.GetCombatActor(characterId);
            return actor is null ? null : EnsureResource(actor);
        }
    }

    public WorldCharacterSkillState GetSkillState(string characterId, string skillId)
    {
        lock (_gate)
        {
            return GetOrCreateSkillState(characterId, skillId);
        }
    }

    public void SetResourceStateForTesting(string characterId, int mp)
    {
        lock (_gate)
        {
            var actor = _movementRuntime.GetCombatActor(characterId)
                ?? throw new InvalidOperationException("Character must be joined before resource state can be set.");
            var clamped = Math.Max(0, Math.Min(mp, actor.Stats.MaxMp));
            _resourcesByCharacterId[characterId] = new WorldCombatResourceState(characterId, clamped, actor.Stats.MaxMp);
        }
    }

    public void SetSkillXpForTesting(string characterId, string skillId, int skillXp)
    {
        lock (_gate)
        {
            var clamped = Math.Max(0, skillXp);
            _skillStates[(characterId, skillId)] = new WorldCharacterSkillState(
                characterId,
                skillId,
                clamped,
                ShieldBashCatalog.ResolveRank(clamped),
                ShieldBashCatalog.MaxRank);
        }
    }

    private static WorldBasicAttackResult Rejected(
        WorldBasicAttackStatus status,
        ulong sequence,
        string message,
            SkillStateChanged? skillStateChanged = null)
    {
        WorldRuntimeTelemetry.RecordDamageRejected(status.ToString());
        return new WorldBasicAttackResult(status, message, sequence, ErrorCode.AttackRejected, null, skillStateChanged);
    }

    private static WorldShieldBashResult CastRejected(
        WorldShieldBashStatus status,
        ulong sequence,
        string message,
        SkillStateChanged? skillStateChanged = null)
    {
        WorldRuntimeTelemetry.RecordDamageRejected(status.ToString());
        return new WorldShieldBashResult(status, message, sequence, ErrorCode.CastRejected, null, skillStateChanged, null);
    }

    private WorldCombatResourceState EnsureResource(WorldCombatActor actor)
    {
        if (_resourcesByCharacterId.TryGetValue(actor.EntityId, out var resource))
        {
            return resource;
        }

        resource = new WorldCombatResourceState(actor.EntityId, actor.Stats.MaxMp, actor.Stats.MaxMp);
        _resourcesByCharacterId[actor.EntityId] = resource;
        return resource;
    }

    private WorldCharacterSkillState AddSkillXp(string characterId, string skillId, int amount)
    {
        var current = GetOrCreateSkillState(characterId, skillId);
        var skillXp = current.SkillXp + amount;
        var next = current with
        {
            SkillXp = skillXp,
            Rank = ShieldBashCatalog.ResolveRank(skillXp)
        };
        _skillStates[(characterId, skillId)] = next;
        return next;
    }

    private WorldCharacterSkillState GetOrCreateSkillState(string characterId, string skillId)
    {
        var key = (characterId, skillId);
        if (_skillStates.TryGetValue(key, out var state))
        {
            return state;
        }

        state = new WorldCharacterSkillState(characterId, skillId, SkillXp: 0, Rank: 1, MaxRank: ShieldBashCatalog.MaxRank);
        _skillStates[key] = state;
        return state;
    }

    private WorldRewardGrantResult? GrantRewardIfKilled(string? killId, string characterId, string skillId = "", int skillXp = 0) =>
        _rewardRuntime is not null && !string.IsNullOrWhiteSpace(killId)
            ? _rewardRuntime.GrantMossSlimeReward(killId, characterId, skillId, skillXp)
            : null;

    private static CharacterProgressed ToCharacterProgressed(WorldCharacterSkillState state) =>
        new()
        {
            CharacterId = state.CharacterId,
            SkillId = state.SkillId,
            SkillXp = (uint)state.SkillXp,
            SkillRank = (uint)state.Rank,
            MaxRank = (uint)state.MaxRank
        };

    private static SkillStateChanged CreateSkillState(string characterId, string skillId, DateTimeOffset now, DateTimeOffset cooldownEndsAt, bool available) =>
        new()
        {
            CharacterId = characterId,
            SkillId = skillId,
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
