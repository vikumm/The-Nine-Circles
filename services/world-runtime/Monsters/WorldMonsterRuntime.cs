using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Monsters;
using Divinity.WorldRuntime.Map;

namespace Divinity.WorldRuntime.Monsters;

public sealed class WorldMonsterRuntime
{
    private readonly object _gate = new();
    private readonly WorldMapCatalog _mapCatalog;
    private readonly TimeProvider _timeProvider;
    private readonly List<WorldMonsterState> _monsters = [];
    private readonly Dictionary<string, WorldMonsterPlayerState> _players = new(StringComparer.Ordinal);
    private ulong _nextSnapshotId = 1;
    private ulong _nextCombatEventId = 1;
    private ulong _nextKillId = 1;

    public WorldMonsterRuntime(WorldMapCatalog mapCatalog, TimeProvider? timeProvider = null)
    {
        _mapCatalog = mapCatalog;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var now = _timeProvider.GetUtcNow();
        foreach (var spawn in mapCatalog.Map.Spawns
            .Where(spawn => spawn.Kind == SpawnKind.Monster && MossSlimeCatalog.IsMossSlime(spawn.EntityTemplateId))
            .Take(MossSlimeCatalog.MaxActiveInstances))
        {
            _monsters.Add(new WorldMonsterState(
                $"monster:{spawn.Id}",
                spawn.Id,
                spawn.EntityTemplateId,
                MossSlimeCatalog.Level1.Level,
                spawn.X,
                spawn.Y,
                now));
        }
    }

    public IReadOnlyList<WorldMonsterState> Monsters
    {
        get
        {
            lock (_gate)
            {
                return _monsters.Select(CloneMonster).ToArray();
            }
        }
    }

    public int ActiveMonsterCount
    {
        get
        {
            lock (_gate)
            {
                return _monsters.Count(monster => monster.Active);
            }
        }
    }

    public void UpsertPlayer(string characterId, double x, double y, int hp = 100, bool connected = true, bool alive = true)
    {
        lock (_gate)
        {
            if (_players.TryGetValue(characterId, out var player))
            {
                player.X = x;
                player.Y = y;
                player.Hp = hp;
                player.Connected = connected;
                player.Alive = alive;
                return;
            }

            _players[characterId] = new WorldMonsterPlayerState(characterId, x, y, hp, connected, alive);
        }
    }

    public void DisconnectPlayer(string characterId)
    {
        lock (_gate)
        {
            if (_players.TryGetValue(characterId, out var player))
            {
                player.Connected = false;
            }
        }
    }

    public void SetPlayerAlive(string characterId, bool alive)
    {
        lock (_gate)
        {
            if (_players.TryGetValue(characterId, out var player))
            {
                player.Alive = alive;
                if (!alive)
                {
                    player.Hp = 0;
                }
            }
        }
    }

    public WorldMonsterDeathResult KillMonster(string entityId, string killerCharacterId)
    {
        lock (_gate)
        {
            var monster = _monsters.FirstOrDefault(candidate => string.Equals(candidate.EntityId, entityId, StringComparison.Ordinal));
            if (monster is null)
            {
                return new WorldMonsterDeathResult(false, "Monster not found.", null, Array.Empty<RewardGranted>());
            }

            if (!monster.Active)
            {
                return new WorldMonsterDeathResult(false, "Monster is already dead.", monster.LastKillId, Array.Empty<RewardGranted>());
            }

            var now = _timeProvider.GetUtcNow();
            var killId = $"kill:{_nextKillId++}";
            monster.AiState = MonsterAiState.Dead;
            monster.Hp = 0;
            monster.TargetCharacterId = null;
            monster.LastKillId = killId;
            monster.RespawnAtUtc = now + MossSlimeCatalog.Level1.RespawnDelay;

            return new WorldMonsterDeathResult(true, "Monster death accepted.", killId, Array.Empty<RewardGranted>());
        }
    }

    public WorldMonsterCombatTarget? GetCombatTarget(string entityId)
    {
        lock (_gate)
        {
            var monster = _monsters.FirstOrDefault(candidate => string.Equals(candidate.EntityId, entityId, StringComparison.Ordinal));
            return monster is null
                ? null
                : new WorldMonsterCombatTarget(
                    monster.EntityId,
                    _mapCatalog.Map.MapId,
                    KnightCatalog.ChannelId,
                    monster.TemplateId,
                    monster.X,
                    monster.Y,
                    monster.Hp,
                    MossSlimeCatalog.Level1.Defense,
                    monster.Active);
        }
    }

    public WorldMonsterDamageResult ApplyDamageToMonster(string entityId, int damage, string attackerCharacterId)
    {
        lock (_gate)
        {
            var monster = _monsters.FirstOrDefault(candidate => string.Equals(candidate.EntityId, entityId, StringComparison.Ordinal));
            if (monster is null)
            {
                return new WorldMonsterDamageResult(false, "Monster not found.", 0, 0, null);
            }

            if (!monster.Active || monster.Hp <= 0)
            {
                return new WorldMonsterDamageResult(false, "Monster is already dead.", 0, monster.Hp, monster.LastKillId);
            }

            var damageApplied = Math.Max(1, damage);
            monster.Hp = Math.Max(0, monster.Hp - damageApplied);
            if (monster.Hp > 0)
            {
                return new WorldMonsterDamageResult(true, "Monster damaged.", damageApplied, monster.Hp, null);
            }

            var now = _timeProvider.GetUtcNow();
            var killId = $"kill:{_nextKillId++}";
            monster.AiState = MonsterAiState.Dead;
            monster.TargetCharacterId = null;
            monster.LastKillId = killId;
            monster.RespawnAtUtc = now + MossSlimeCatalog.Level1.RespawnDelay;

            return new WorldMonsterDamageResult(true, "Monster killed.", damageApplied, monster.Hp, killId);
        }
    }

    public WorldMonsterTickResult Tick(TimeSpan delta)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            var combatEvents = new List<CombatEvent>();
            var rewardEvents = new List<RewardGranted>();

            foreach (var monster in _monsters)
            {
                TickMonster(monster, delta, now, combatEvents);
            }

            return new WorldMonsterTickResult(CreateSnapshot(), combatEvents, rewardEvents);
        }
    }

    private void TickMonster(WorldMonsterState monster, TimeSpan delta, DateTimeOffset now, List<CombatEvent> combatEvents)
    {
        if (monster.AiState == MonsterAiState.Dead)
        {
            TryRespawn(monster, now);
            return;
        }

        if (monster.AiState == MonsterAiState.Return)
        {
            TickReturn(monster, delta);
            return;
        }

        var target = ResolveTarget(monster);
        if (target is null)
        {
            if (monster.TargetCharacterId is not null || monster.AiState is MonsterAiState.Chase or MonsterAiState.Attack)
            {
                StartReturn(monster);
                return;
            }

            TickIdleOrWander(monster, now);
            return;
        }

        if (IsOutsideLeash(monster, target))
        {
            StartReturn(monster);
            return;
        }

        monster.TargetCharacterId = target.CharacterId;
        var distanceToTarget = Distance(monster.X, monster.Y, target.X, target.Y);
        if (distanceToTarget <= MossSlimeCatalog.Level1.AttackRange)
        {
            TickAttack(monster, target, now, combatEvents);
            return;
        }

        TickChase(monster, target, delta, now);
    }

    private void TickIdleOrWander(WorldMonsterState monster, DateTimeOffset now)
    {
        monster.TargetCharacterId = null;
        if (now < monster.NextWanderAtUtc)
        {
            return;
        }

        monster.AiState = monster.AiState == MonsterAiState.Idle
            ? MonsterAiState.Wander
            : MonsterAiState.Idle;
        monster.NextWanderAtUtc = now + MossSlimeCatalog.WanderInterval;
    }

    private void TickChase(WorldMonsterState monster, WorldMonsterPlayerState target, TimeSpan delta, DateTimeOffset now)
    {
        monster.AiState = MonsterAiState.Chase;

        if (now - monster.LastPathRecalculatedAtUtc < MossSlimeCatalog.PathRecalculationInterval)
        {
            return;
        }

        monster.LastPathRecalculatedAtUtc = now;
        monster.PathRecalculationCount++;

        var step = MossSlimeCatalog.Level1.SpeedUnitsPerSecond * Math.Max(0, delta.TotalSeconds);
        MoveToward(monster, target.X, target.Y, step);
    }

    private void TickAttack(WorldMonsterState monster, WorldMonsterPlayerState target, DateTimeOffset now, List<CombatEvent> combatEvents)
    {
        monster.AiState = MonsterAiState.Attack;
        monster.TargetCharacterId = target.CharacterId;

        if (now < monster.NextAttackAtUtc)
        {
            return;
        }

        target.Hp = Math.Max(0, target.Hp - MossSlimeCatalog.Level1.Attack);
        if (target.Hp == 0)
        {
            target.Alive = false;
        }

        monster.NextAttackAtUtc = now + MossSlimeCatalog.Level1.AttackCooldown;
        combatEvents.Add(new CombatEvent
        {
            EventId = $"combat:{_nextCombatEventId++}",
            SourceEntityId = monster.EntityId,
            TargetEntityId = target.CharacterId,
            SkillId = MossSlimeCatalog.BasicAttackSkillId,
            Result = CombatResult.Hit,
            Damage = MossSlimeCatalog.Level1.Attack,
            TargetHp = target.Hp
        });
    }

    private void TickReturn(WorldMonsterState monster, TimeSpan delta)
    {
        monster.TargetCharacterId = null;
        monster.Hp = MossSlimeCatalog.Level1.MaxHp;

        var distanceToSpawn = Distance(monster.X, monster.Y, monster.SpawnX, monster.SpawnY);
        if (distanceToSpawn <= 0.000001d)
        {
            monster.AiState = MonsterAiState.Idle;
            return;
        }

        var step = MossSlimeCatalog.Level1.SpeedUnitsPerSecond * Math.Max(0, delta.TotalSeconds);
        MoveToward(monster, monster.SpawnX, monster.SpawnY, step);

        if (Distance(monster.X, monster.Y, monster.SpawnX, monster.SpawnY) <= 0.000001d)
        {
            monster.AiState = MonsterAiState.Idle;
        }
    }

    private void TryRespawn(WorldMonsterState monster, DateTimeOffset now)
    {
        if (monster.RespawnAtUtc is null || now < monster.RespawnAtUtc)
        {
            return;
        }

        monster.X = monster.SpawnX;
        monster.Y = monster.SpawnY;
        monster.Hp = MossSlimeCatalog.Level1.MaxHp;
        monster.AiState = MonsterAiState.Idle;
        monster.TargetCharacterId = null;
        monster.RespawnAtUtc = null;
        monster.NextAttackAtUtc = now;
        monster.LastPathRecalculatedAtUtc = now;
        monster.NextWanderAtUtc = now + MossSlimeCatalog.WanderInterval;
    }

    private WorldMonsterPlayerState? ResolveTarget(WorldMonsterState monster)
    {
        if (monster.TargetCharacterId is not null
            && _players.TryGetValue(monster.TargetCharacterId, out var existing)
            && existing.IsValidTarget)
        {
            return existing;
        }

        return _players.Values
            .Where(player => player.IsValidTarget)
            .Where(player => Distance(monster.X, monster.Y, player.X, player.Y) <= MossSlimeCatalog.Level1.AggroRadius)
            .OrderBy(player => Distance(monster.X, monster.Y, player.X, player.Y))
            .FirstOrDefault();
    }

    private static bool IsOutsideLeash(WorldMonsterState monster, WorldMonsterPlayerState target) =>
        Distance(monster.SpawnX, monster.SpawnY, target.X, target.Y) > MossSlimeCatalog.Level1.LeashRadius
        || Distance(monster.SpawnX, monster.SpawnY, monster.X, monster.Y) > MossSlimeCatalog.Level1.LeashRadius;

    private static void StartReturn(WorldMonsterState monster)
    {
        monster.AiState = MonsterAiState.Return;
        monster.TargetCharacterId = null;
        monster.Hp = MossSlimeCatalog.Level1.MaxHp;
    }

    private void MoveToward(WorldMonsterState monster, double targetX, double targetY, double step)
    {
        var deltaX = targetX - monster.X;
        var deltaY = targetY - monster.Y;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance <= 0.000001d)
        {
            return;
        }

        var appliedStep = Math.Min(step, distance);
        var nextX = monster.X + (deltaX / distance) * appliedStep;
        var nextY = monster.Y + (deltaY / distance) * appliedStep;
        if (_mapCatalog.SegmentTouchesBlockedOrOutOfBounds(monster.X, monster.Y, nextX, nextY))
        {
            return;
        }

        monster.X = nextX;
        monster.Y = nextY;
    }

    private WorldSnapshot CreateSnapshot()
    {
        var snapshot = new WorldSnapshot
        {
            SnapshotId = _nextSnapshotId++,
            MapId = _mapCatalog.Map.MapId
        };

        foreach (var monster in _monsters.Where(monster => monster.Active))
        {
            snapshot.Entities.Add(new EntityState
            {
                EntityId = monster.EntityId,
                Kind = EntityKind.Monster,
                Position = new Vector2
                {
                    X = (float)monster.X,
                    Y = (float)monster.Y
                },
                Level = (uint)monster.Level,
                Hp = monster.Hp
            });
        }

        return snapshot;
    }

    private static WorldMonsterState CloneMonster(WorldMonsterState monster) =>
        new(
            monster.EntityId,
            monster.SpawnId,
            monster.TemplateId,
            monster.Level,
            monster.SpawnX,
            monster.SpawnY,
            monster.LastPathRecalculatedAtUtc)
        {
            X = monster.X,
            Y = monster.Y,
            Hp = monster.Hp,
            AiState = monster.AiState,
            TargetCharacterId = monster.TargetCharacterId,
            LastPathRecalculatedAtUtc = monster.LastPathRecalculatedAtUtc,
            NextWanderAtUtc = monster.NextWanderAtUtc,
            NextAttackAtUtc = monster.NextAttackAtUtc,
            RespawnAtUtc = monster.RespawnAtUtc,
            LastKillId = monster.LastKillId,
            PathRecalculationCount = monster.PathRecalculationCount
        };

    private static double Distance(double startX, double startY, double endX, double endY)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}
