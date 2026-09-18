using System.Diagnostics;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway.Observability;
using Divinity.GameGateway.Protocol;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.GameRules.Combat;
using Divinity.GameRules.Equipment;
using Divinity.GameRules.Inventory;
using Divinity.GameRules.Monsters;
using Divinity.GameRules.Rewards;
using Divinity.WorldRuntime.Combat;
using Divinity.WorldRuntime.Equipment;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Monsters;
using Divinity.WorldRuntime.Movement;
using Divinity.WorldRuntime.Rewards;

namespace Divinity.LoadBots;

public sealed class LoadBotRunner
{
    public async Task<LoadBotReport> RunAsync(LoadBotOptions options, CancellationToken cancellationToken)
    {
        var builder = new LoadBotReportBuilder(DateTimeOffset.UtcNow);

        for (var iteration = 1; iteration <= options.Iterations; iteration++)
        {
            foreach (var step in options.RampSteps)
            {
                var results = await RunBotGroupAsync($"ramp-{iteration}-{step}", step, builder, cancellationToken);
                builder.AddCheck(
                    $"ramp {step} bots iteration {iteration}",
                    results.All(result => result.Success),
                    $"{results.Count(result => result.Success)}/{step} bots completed full flow");
            }
        }

        var combatResults = await RunBotGroupAsync("combat-50", options.ConcurrentCombatBots, builder, cancellationToken);
        builder.AddCheck(
            "50 bots can fight simultaneously",
            options.ConcurrentCombatBots >= 50
                ? combatResults.Count(result => result.Success) >= 50
                : combatResults.All(result => result.Success),
            $"{combatResults.Count(result => result.Success)}/{options.ConcurrentCombatBots} concurrent combat bots succeeded");

        var batchReconnect = await RunBatchReconnectAsync(options, builder, cancellationToken);
        builder.AddCheck(
            "20 percent disconnect and reconnect together",
            batchReconnect.Reconnected == batchReconnect.Disconnected && batchReconnect.Disconnected == batchReconnect.ExpectedDisconnected,
            $"{batchReconnect.Reconnected}/{batchReconnect.ExpectedDisconnected} disconnected bots reconnected");

        if (options.RunReplayScenario)
        {
            var replay = await RunSingleBotAsync("replay", builder, runReplay: true, slowClientDelayMs: 0, saveShutdownCheckpoint: false, cancellationToken);
            builder.AddBotResult(replay);
            builder.AddCheck("replay message rejected", replay.ReplayRejected, replay.ReplayRejected ? "sequence replay produced no duplicate reward" : "sequence replay was not rejected");
        }

        if (options.RunSlowClientScenario)
        {
            var slow = await RunSingleBotAsync("slow-client", builder, runReplay: false, options.SlowClientDelayMs, saveShutdownCheckpoint: false, cancellationToken);
            builder.AddBotResult(slow);
            builder.AddCheck("slow client completes flow", slow.Success, $"delay={options.SlowClientDelayMs}ms success={slow.Success}");
        }

        if (options.RunGatewayRestartScenario)
        {
            var restart = await RunGatewayRestartAsync(builder, cancellationToken);
            builder.AddCheck("gateway restart during load preserves reconnect", restart, restart ? "reconnect accepted after GatewaySessionManager restart" : "reconnect failed after restart");
        }

        if (options.RunWorldShutdownScenario)
        {
            var shutdown = await RunWorldShutdownAsync(builder, cancellationToken);
            builder.AddCheck("world runtime shutdown is graceful", shutdown, shutdown ? "checkpoint stored on shutdown" : "shutdown checkpoint was not stored");
        }

        if (options.RunHardeningScenario)
        {
            await RunHardeningScenarioAsync(builder, cancellationToken);
        }

        if (options.RunProfilingScenario)
        {
            RunProfilingScenario(builder);
        }

        if (options.SoakDurationSeconds > 0)
        {
            await RunSoakScenarioAsync(options, builder, cancellationToken);
        }

        var duplicateRewards = builder.Build(options, DateTimeOffset.UtcNow)
            .BotResults
            .Sum(result => result.DuplicateRewardRecords);
        builder.AddCheck("duplicated reward records equals zero", duplicateRewards == 0, $"duplicates={duplicateRewards}");

        return builder.Build(options, DateTimeOffset.UtcNow);
    }

    private static async Task<IReadOnlyList<LoadBotResult>> RunBotGroupAsync(
        string groupName,
        int botCount,
        LoadBotReportBuilder builder,
        CancellationToken cancellationToken)
    {
        var tasks = Enumerable.Range(1, botCount)
            .Select(index => RunSingleBotAsync($"{groupName}-bot-{index:D3}", builder, runReplay: true, slowClientDelayMs: 0, saveShutdownCheckpoint: false, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            builder.AddBotResult(result);
        }

        return results;
    }

    private static async Task<BatchReconnectResult> RunBatchReconnectAsync(
        LoadBotOptions options,
        LoadBotReportBuilder builder,
        CancellationToken cancellationToken)
    {
        var expected = Math.Max(1, options.ResilienceBots * options.DisconnectPercent / 100);
        var tasks = Enumerable.Range(1, expected)
            .Select(index => RunReconnectOnlyAsync($"batch-reconnect-{index:D3}", builder, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        return new BatchReconnectResult(expected, results.Length, results.Count(result => result));
    }

    private static async Task<bool> RunReconnectOnlyAsync(
        string botId,
        LoadBotReportBuilder builder,
        CancellationToken cancellationToken)
    {
        using var environment = await LoadBotEnvironment.CreateAsync(botId, cancellationToken);
        var session = await AuthenticateAsync(environment, $"{botId}-a", builder, cancellationToken);
        var character = await CreateKnightAsync(environment, botId, cancellationToken);

        builder.CountMessage("JoinWorld");
        var join = environment.Sessions.TryJoin(session.ConnectionId, character.CharacterId);
        if (join.Status != JoinLeaseStatus.Joined || join.ReconnectToken is null)
        {
            builder.CountMessage("ServerError");
            return false;
        }

        _ = await environment.Movement.JoinAsync(character, cancellationToken);
        builder.CountMessage("WorldSnapshot");

        environment.Sessions.Disconnect(session.ConnectionId, "disconnect_abrupt", preserveLeaseForReconnect: true);

        var reconnectSession = await AuthenticateAsync(environment, $"{botId}-b", builder, cancellationToken);
        builder.CountMessage("ReconnectRequest");
        var reconnect = environment.Sessions.TryReconnect(reconnectSession.ConnectionId, join.ReconnectToken.Token, session.ConnectionId);
        if (reconnect.Status != ReconnectLeaseStatus.Reconnected)
        {
            builder.CountMessage("ServerError");
        }

        return reconnect.Status == ReconnectLeaseStatus.Reconnected;
    }

    private static async Task<bool> RunGatewayRestartAsync(LoadBotReportBuilder builder, CancellationToken cancellationToken)
    {
        using var environment = await LoadBotEnvironment.CreateAsync("gateway-restart", cancellationToken);
        var session = await AuthenticateAsync(environment, "restart-a", builder, cancellationToken);
        var character = await CreateKnightAsync(environment, "gateway-restart", cancellationToken);

        builder.CountMessage("JoinWorld");
        var join = environment.Sessions.TryJoin(session.ConnectionId, character.CharacterId);
        if (join.Status != JoinLeaseStatus.Joined || join.ReconnectToken is null)
        {
            builder.CountMessage("ServerError");
            return false;
        }

        environment.Sessions.Disconnect(session.ConnectionId, "disconnect_abrupt", preserveLeaseForReconnect: true);
        var restartedSessions = new GatewaySessionManager(environment.Time, Path.Combine(environment.StorePath, "gateway"));
        var reconnectSession = await AuthenticateAsync(environment with { Sessions = restartedSessions }, "restart-b", builder, cancellationToken);

        builder.CountMessage("ReconnectRequest");
        var reconnect = restartedSessions.TryReconnect(reconnectSession.ConnectionId, join.ReconnectToken.Token, session.ConnectionId);
        if (reconnect.Status != ReconnectLeaseStatus.Reconnected)
        {
            builder.CountMessage("ServerError");
        }

        return reconnect.Status == ReconnectLeaseStatus.Reconnected;
    }

    private static async Task<bool> RunWorldShutdownAsync(LoadBotReportBuilder builder, CancellationToken cancellationToken)
    {
        using var environment = await LoadBotEnvironment.CreateAsync("world-shutdown", cancellationToken);
        var character = await CreateKnightAsync(environment, "world-shutdown", cancellationToken);
        _ = await environment.Movement.JoinAsync(character, cancellationToken);

        await builder.MeasureAsync("world_shutdown", async () =>
        {
            await environment.Movement.SaveAllCheckpointsAsync("shutdown", cancellationToken);
            return true;
        });

        var checkpoint = await environment.CharacterStore.GetCheckpointAsync(character.CharacterId, cancellationToken);
        return checkpoint?.Reason == "shutdown";
    }

    private static async Task RunHardeningScenarioAsync(LoadBotReportBuilder builder, CancellationToken cancellationToken)
    {
        var oversized = ProtocolV1Handler.HandleClientEnvelope(new byte[ProtocolConstants.MaxEnvelopeBytes + 1]);
        builder.AddCheck(
            "payload larger than 64 KiB rejected before parse",
            oversized.Envelope.ServerError.Code == ErrorCode.PayloadTooLarge,
            oversized.Envelope.ServerError.Code.ToString());

        var truncated = ProtocolV1Handler.HandleClientEnvelope([8, 1, 18]);
        builder.AddCheck(
            "truncated protobuf rejected",
            truncated.Envelope.ServerError.Code == ErrorCode.MalformedPayload,
            truncated.Envelope.ServerError.Code.ToString());

        var limiter = new AnonymousHandshakeRateLimiter(new ManualLoadBotTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        var handshakeSpamBlocked = limiter.TryAcquire("127.0.0.1")
            && limiter.TryAcquire("127.0.0.1")
            && limiter.TryAcquire("127.0.0.1")
            && !limiter.TryAcquire("127.0.0.1");
        builder.AddCheck("handshake spam is rate limited", handshakeSpamBlocked, "limit=3/minute per remote address");

        using (var environment = await LoadBotEnvironment.CreateAsync("vs020-rate-limits", cancellationToken))
        {
            var session = await AuthenticateAsync(environment, "rate-limit", builder, cancellationToken);
            builder.AddCheck("rate limit move category", ExhaustsRateLimit(environment.Sessions, session.ConnectionId, GatewayRateLimitCategory.Move), "20/s");
            builder.AddCheck("rate limit combat category", ExhaustsRateLimit(environment.Sessions, session.ConnectionId, GatewayRateLimitCategory.Combat), "12/s");
            builder.AddCheck("rate limit inventory category", ExhaustsRateLimit(environment.Sessions, session.ConnectionId, GatewayRateLimitCategory.Inventory), "6/s");
            builder.AddCheck("rate limit heartbeat category", ExhaustsRateLimit(environment.Sessions, session.ConnectionId, GatewayRateLimitCategory.Heartbeat), "4/s");
            builder.AddCheck("rate limit reconnect category", ExhaustsRateLimit(environment.Sessions, session.ConnectionId, GatewayRateLimitCategory.Reconnect), "2/30s");
        }

        await RunOffensiveDefensiveScenarioAsync(builder, cancellationToken);

        var structuredLog = GatewayTelemetry.CreateLog(
            "Development",
            "conn-vs020",
            "acct_pseudo",
            "char-vs020",
            "training-field-01",
            "kill_reward",
            "accepted",
            ErrorCode.Unspecified,
            latencyMs: 3.5d);
        var serializedLog = structuredLog.ToString() ?? string.Empty;
        builder.AddCheck(
            "structured logs contain minimum fields without secrets",
            structuredLog.TimestampUtc.Offset == TimeSpan.Zero
                && structuredLog.Service == GatewayTelemetry.ServiceName
                && !string.IsNullOrWhiteSpace(structuredLog.ConnectionId)
                && !GatewayTelemetry.ContainsForbiddenSecret(serializedLog),
            "timestamp/service/environment/trace/connection/account/character/map/event/result/error/latency");
    }

    private static async Task RunOffensiveDefensiveScenarioAsync(LoadBotReportBuilder builder, CancellationToken cancellationToken)
    {
        using var environment = await LoadBotEnvironment.CreateAsync("vs020-offensive", cancellationToken);
        var session = await AuthenticateAsync(environment, "offensive-a", builder, cancellationToken);
        var character = await CreateKnightAsync(environment, "vs020-offensive", cancellationToken);

        await environment.CharacterStore.StoreCheckpointAsync(
            new CharacterCheckpointRecord(character.CharacterId, environment.Map.Map.MapId, new CharacterPosition(31, 40), 0, "offensive_wall", environment.Time.GetUtcNow(), character.Stats.MaxHp, character.Stats.MaxMp, character.Stats.Level),
            cancellationToken);
        var joined = await environment.Movement.JoinAsync(character, cancellationToken);
        environment.Monsters.UpsertPlayer(character.CharacterId, (double)joined.Position.X, (double)joined.Position.Y, joined.Stats.Hp);

        var wall = await environment.Movement.ApplyMoveAsync(
            character.CharacterId,
            sequence: 1,
            clientTick: 1,
            new MoveIntent { Mode = MovementMode.ClickTarget, TargetX = 41, TargetY = 40 },
            cancellationToken);
        builder.AddCheck("offensive move through wall rejected", wall.Status == WorldMovementStatus.Collision, wall.Status.ToString());

        var replayMove = await environment.Movement.ApplyMoveAsync(
            character.CharacterId,
            sequence: 1,
            clientTick: 2,
            new MoveIntent { Mode = MovementMode.Direction, DirectionX = 1, DirectionY = 0 },
            cancellationToken);
        builder.AddCheck("offensive repeated movement sequence rejected", replayMove.Status == WorldMovementStatus.SequenceReplay, replayMove.Status.ToString());

        var missingTarget = environment.Combat.ApplyBasicAttack(
            character.CharacterId,
            sequence: 2,
            new AttackIntent { TargetEntityId = "other-map:monster", SkillId = BasicSlashCatalog.SkillId, ActionId = "other-map-target" });
        builder.AddCheck("offensive target id outside map rejected", missingTarget.Status == WorldBasicAttackStatus.TargetNotFound, missingTarget.Status.ToString());

        using var combatEnvironment = await LoadBotEnvironment.CreateAsync("vs020-combat-offensive", cancellationToken);
        var combatSession = await AuthenticateAsync(combatEnvironment, "combat-offensive-a", builder, cancellationToken);
        _ = combatSession;
        var combatCharacter = await CreateKnightAsync(combatEnvironment, "combat-offensive", cancellationToken);
        await PositionNearFirstMonsterAsync(combatEnvironment, combatCharacter, cancellationToken);
        _ = await combatEnvironment.Movement.JoinAsync(combatCharacter, cancellationToken);
        var targetEntityId = combatEnvironment.Monsters.Monsters.First().EntityId;

        combatEnvironment.Combat.SetResourceStateForTesting(combatCharacter.CharacterId, 0);
        var noMp = combatEnvironment.Combat.ApplyShieldBash(
            combatCharacter.CharacterId,
            sequence: 1,
            new CastIntent { TargetEntityId = targetEntityId, SkillId = ShieldBashCatalog.SkillId, ActionId = "no-mp" });
        builder.AddCheck("offensive CastIntent without MP rejected", noMp.Status == WorldShieldBashStatus.InsufficientMp, noMp.Status.ToString());

        combatEnvironment.Combat.SetResourceStateForTesting(combatCharacter.CharacterId, combatCharacter.Stats.MaxMp);
        var firstCast = combatEnvironment.Combat.ApplyShieldBash(
            combatCharacter.CharacterId,
            sequence: 2,
            new CastIntent { TargetEntityId = targetEntityId, SkillId = ShieldBashCatalog.SkillId, ActionId = "cooldown-first" });
        var cooldown = combatEnvironment.Combat.ApplyShieldBash(
            combatCharacter.CharacterId,
            sequence: 3,
            new CastIntent { TargetEntityId = targetEntityId, SkillId = ShieldBashCatalog.SkillId, ActionId = "cooldown-zero" });
        builder.AddCheck("offensive client cooldown zero rejected", firstCast.Accepted && cooldown.Status == WorldShieldBashStatus.Cooldown, cooldown.Status.ToString());

        using var rangeEnvironment = await LoadBotEnvironment.CreateAsync("vs020-range-offensive", cancellationToken);
        var rangeCharacter = await CreateKnightAsync(rangeEnvironment, "range-offensive", cancellationToken);
        _ = await rangeEnvironment.Movement.JoinAsync(rangeCharacter, cancellationToken);
        var outOfRange = rangeEnvironment.Combat.ApplyBasicAttack(
            rangeCharacter.CharacterId,
            sequence: 1,
            new AttackIntent { TargetEntityId = rangeEnvironment.Monsters.Monsters.First().EntityId, SkillId = BasicSlashCatalog.SkillId, ActionId = "out-of-range" });
        builder.AddCheck("offensive attack out of range rejected", outOfRange.Status == WorldBasicAttackStatus.OutOfRange, outOfRange.Status.ToString());

        var actor = InventoryActor.FromCharacter(character);
        var inventory = environment.Inventory.GetOrCreateInventory(actor);
        var missingEquip = environment.Inventory.EquipItem(new InventoryEquipItemCommand(actor, "item_missing", InventoryEquipmentSlot.OffHand, inventory.InventoryVersion));
        builder.AddCheck("offensive equip missing item rejected", missingEquip.Status == InventoryOperationStatus.ItemNotFound, missingEquip.Status.ToString());

        var firstReward = environment.Rewards.GrantMossSlimeReward("kill:vs020-duplicate", character.CharacterId);
        var duplicateReward = environment.Rewards.GrantMossSlimeReward("kill:vs020-duplicate", character.CharacterId);
        var rewards = environment.Rewards.ReadDocumentForTesting();
        builder.AddCheck(
            "offensive repeated RewardGranted blocked",
            firstReward.Status == WorldRewardGrantStatus.Granted
                && duplicateReward.Status == WorldRewardGrantStatus.AlreadyGranted
                && rewards.RewardGrants.Count(grant => grant.RewardKey == firstReward.RewardKey) == 1,
            duplicateReward.Status.ToString());

        var competing = await LoadBotEnvironment.CreateAsync("vs020-session-conflict", cancellationToken);
        using (competing)
        {
            var first = await AuthenticateAsync(competing, "session-a", builder, cancellationToken);
            var second = await AuthenticateAsync(competing, "session-b", builder, cancellationToken);
            var competingCharacter = await CreateKnightAsync(competing, "session-conflict", cancellationToken);
            var firstJoin = competing.Sessions.TryJoin(first.ConnectionId, competingCharacter.CharacterId);
            var secondJoin = competing.Sessions.TryJoin(second.ConnectionId, competingCharacter.CharacterId);
            builder.AddCheck("offensive two connections same character rejected", firstJoin.Status == JoinLeaseStatus.Joined && secondJoin.Status == JoinLeaseStatus.LeaseConflict, secondJoin.Status.ToString());
        }

        var reconnectDuringReward = await RunSingleBotAsync("vs020-reconnect-during-monster-death", builder, runReplay: true, slowClientDelayMs: 0, saveShutdownCheckpoint: false, cancellationToken);
        builder.AddBotResult(reconnectDuringReward);
        builder.AddCheck("offensive reconnect during monster death has no duplicate reward", reconnectDuringReward.Success && reconnectDuringReward.DuplicateRewardRecords == 0, $"duplicates={reconnectDuringReward.DuplicateRewardRecords}");

        var issue = await environment.Tickets.IssueAsync(
            new GameTicketIssueCommand(environment.AccountId, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, "clock-change"),
            cancellationToken);
        environment.Time.Advance(GameTicketDefaults.TimeToLive + TimeSpan.FromSeconds(1));
        var expired = await environment.Tickets.ConsumeAsync(
            new GameTicketConsumeCommand(issue.GameTicket!, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, "clock-change"),
            cancellationToken);
        builder.AddCheck("offensive client clock change cannot extend ticket", expired.Status == GameTicketConsumeStatus.ExpiredTicket, expired.Status.ToString());
    }

    private static bool ExhaustsRateLimit(GatewaySessionManager sessions, string connectionId, GatewayRateLimitCategory category)
    {
        var rule = GatewaySessionDefaults.GetRateLimitRule(category);
        for (var index = 0; index < rule.Limit; index++)
        {
            if (sessions.TryAcquireMessageIntent(connectionId, category).Status != GatewayMessageRateLimitStatus.Accepted)
            {
                return false;
            }
        }

        return sessions.TryAcquireMessageIntent(connectionId, category).Status == GatewayMessageRateLimitStatus.RateLimited;
    }

    private static void RunProfilingScenario(LoadBotReportBuilder builder)
    {
        const double targetFrameBudgetMs = 1000d / 60d;
        builder.AddCheck("client QA FPS target configured", targetFrameBudgetMs <= 16.7d, "target=60 FPS at 1920x1080");

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var samples = new List<long>();
        for (var index = 0; index < 40; index++)
        {
            var started = Stopwatch.GetTimestamp();
            _ = GC.GetTotalMemory(forceFullCollection: false);
            samples.Add((long)TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
        }

        var after = GC.GetTotalMemory(forceFullCollection: true);
        var p95 = samples.Order().ElementAt((int)Math.Ceiling(samples.Count * 0.95d) - 1);
        builder.AddCheck("profiling GC sampling p95 below 4 ms", p95 < 4, $"p95={p95}ms");
        builder.AddCheck("profiling memory has no continuous growth", after - before < 32 * 1024 * 1024, $"delta_bytes={after - before}");
    }

    private static async Task RunSoakScenarioAsync(LoadBotOptions options, LoadBotReportBuilder builder, CancellationToken cancellationToken)
    {
        var duration = TimeSpan.FromSeconds(options.SoakDurationSeconds);
        var stopwatch = Stopwatch.StartNew();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var iterations = 0;
        var failures = 0;

        do
        {
            var result = await RunSingleBotAsync($"soak-{iterations + 1:D4}", builder, runReplay: true, slowClientDelayMs: 0, saveShutdownCheckpoint: true, cancellationToken);
            builder.AddBotResult(result);
            if (!result.Success)
            {
                failures++;
            }

            iterations++;
        }
        while (stopwatch.Elapsed < duration && failures == 0);

        var after = GC.GetTotalMemory(forceFullCollection: true);
        builder.AddCheck("soak executes without failures", failures == 0 && iterations > 0, $"iterations={iterations} failures={failures}");
        builder.AddCheck("soak memory has no leak signal", after - before < 64 * 1024 * 1024, $"duration_seconds={options.SoakDurationSeconds} delta_bytes={after - before}");
    }

    private static async Task<LoadBotResult> RunSingleBotAsync(
        string botId,
        LoadBotReportBuilder builder,
        bool runReplay,
        int slowClientDelayMs,
        bool saveShutdownCheckpoint,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        try
        {
            using var environment = await LoadBotEnvironment.CreateAsync(botId, cancellationToken);
            var session = await AuthenticateAsync(environment, $"{botId}-a", builder, cancellationToken);
            var character = await CreateKnightAsync(environment, botId, cancellationToken);
            await PositionNearFirstMonsterAsync(environment, character, cancellationToken);

            builder.CountMessage("JoinWorld");
            var joinLease = builder.Measure("join_lease", () => environment.Sessions.TryJoin(session.ConnectionId, character.CharacterId));
            if (joinLease.Status != JoinLeaseStatus.Joined || joinLease.ReconnectToken is null)
            {
                failures.Add($"JoinWorld failed: {joinLease.Status} {joinLease.Message}");
                builder.CountMessage("ServerError");
                return CreateFailed(botId, failures);
            }

            var join = await builder.MeasureAsync("world_join", () => environment.Movement.JoinAsync(character, cancellationToken));
            builder.CountMessage("WorldSnapshot");
            environment.Monsters.UpsertPlayer(join.CharacterId, (double)join.Position.X, (double)join.Position.Y, hp: join.Stats.Hp);

            if (slowClientDelayMs > 0)
            {
                await Task.Delay(slowClientDelayMs, cancellationToken);
            }

            builder.CountMessage("MoveIntent");
            var moved = await builder.MeasureAsync("move_intent", () => environment.Movement.ApplyMoveAsync(
                character.CharacterId,
                sequence: 1,
                clientTick: 1,
                new MoveIntent { Mode = MovementMode.Direction, DirectionX = -1, DirectionY = 0 },
                cancellationToken));
            if (!moved.Accepted)
            {
                failures.Add($"MoveIntent failed: {moved.Status} {moved.Message}");
                builder.CountMessage("ServerError");
            }
            environment.Monsters.UpsertPlayer(character.CharacterId, (double)moved.Position.X, (double)moved.Position.Y, hp: join.Stats.Hp);

            builder.CountMessage("Heartbeat");
            var heartbeat = builder.Measure("heartbeat", () => environment.Sessions.RenewHeartbeat(session.ConnectionId));
            if (heartbeat.Status != HeartbeatLeaseStatus.Renewed)
            {
                failures.Add($"Heartbeat failed: {heartbeat.Status} {heartbeat.Message}");
                builder.CountMessage("ServerError");
            }

            var targetEntityId = environment.Monsters.Monsters.First().EntityId;
            builder.CountMessage("AttackIntent");
            var firstAttack = builder.Measure("attack_intent", () => environment.Combat.ApplyBasicAttack(
                character.CharacterId,
                sequence: 2,
                new AttackIntent
                {
                    TargetEntityId = targetEntityId,
                    SkillId = BasicSlashCatalog.SkillId,
                    ActionId = $"{botId}-attack-1"
                }));
            CountCombatResponse(builder, firstAttack.CombatEvent, firstAttack.RewardGranted, firstAttack.InventoryDelta, firstAttack.RewardProgressed, firstAttack.Accepted);
            if (!firstAttack.Accepted)
            {
                failures.Add($"AttackIntent failed: {firstAttack.Status} {firstAttack.Message}");
            }

            environment.Time.Advance(BasicSlashCatalog.Cooldown + TimeSpan.FromMilliseconds(1));

            builder.CountMessage("CastIntent");
            var shieldBash = builder.Measure("cast_intent", () => environment.Combat.ApplyShieldBash(
                character.CharacterId,
                sequence: 3,
                new CastIntent
                {
                    TargetEntityId = targetEntityId,
                    SkillId = ShieldBashCatalog.SkillId,
                    ActionId = $"{botId}-shield-bash"
                }));
            CountCombatResponse(builder, shieldBash.CombatEvent, shieldBash.RewardGranted, shieldBash.InventoryDelta, shieldBash.RewardProgressed, shieldBash.Accepted);
            if (shieldBash.CharacterProgressed is not null)
            {
                builder.CountMessage("CharacterProgressed");
            }
            if (!shieldBash.Accepted)
            {
                failures.Add($"CastIntent failed: {shieldBash.Status} {shieldBash.Message}");
            }

            environment.Time.Advance(BasicSlashCatalog.Cooldown + TimeSpan.FromMilliseconds(1));

            builder.CountMessage("AttackIntent");
            var killingAttack = builder.Measure("attack_intent", () => environment.Combat.ApplyBasicAttack(
                character.CharacterId,
                sequence: 4,
                new AttackIntent
                {
                    TargetEntityId = targetEntityId,
                    SkillId = BasicSlashCatalog.SkillId,
                    ActionId = $"{botId}-attack-2"
                }));
            CountCombatResponse(builder, killingAttack.CombatEvent, killingAttack.RewardGranted, killingAttack.InventoryDelta, killingAttack.RewardProgressed, killingAttack.Accepted);
            if (!killingAttack.Accepted || string.IsNullOrWhiteSpace(killingAttack.CombatEvent?.KillId))
            {
                failures.Add($"Killing AttackIntent failed: {killingAttack.Status} {killingAttack.Message}");
            }

            var replayRejected = true;
            if (runReplay)
            {
                builder.CountMessage("AttackIntent");
                var replay = builder.Measure("replay_attack_intent", () => environment.Combat.ApplyBasicAttack(
                    character.CharacterId,
                    sequence: 4,
                    new AttackIntent
                    {
                        TargetEntityId = targetEntityId,
                        SkillId = BasicSlashCatalog.SkillId,
                        ActionId = $"{botId}-attack-2-replay"
                    }));
                replayRejected = replay.Status == WorldBasicAttackStatus.SequenceReplay;
                if (!replayRejected)
                {
                    failures.Add($"Replay was not rejected: {replay.Status}");
                }
                builder.CountMessage(replayRejected ? "ServerError" : "CombatEvent");
            }

            var rewardDocument = environment.Rewards.ReadDocumentForTesting();
            var duplicateRewards = CountDuplicateRewardRecords(rewardDocument);
            if (duplicateRewards > 0)
            {
                failures.Add($"Duplicate reward records detected: {duplicateRewards}");
            }

            var rewardGrant = rewardDocument.RewardGrants.SingleOrDefault(grant => grant.CharacterId == character.CharacterId);
            if (rewardGrant is null)
            {
                failures.Add("RewardGranted was not committed.");
            }
            else if (rewardGrant.ItemInstanceIds.Count == 0)
            {
                failures.Add("Moss Slime did not grant an item in deterministic loot.");
            }

            var actor = InventoryActor.FromCharacter(character);
            var inventoryItem = environment.Inventory.CreateWoodenShieldForTesting(actor, EquipmentRarity.Rare);
            var inventoryVersion = environment.Inventory.GetOrCreateInventory(actor).InventoryVersion;
            builder.CountMessage("EquipItemIntent");
            var equip = builder.Measure("equip_item_intent", () => environment.Inventory.EquipItem(new InventoryEquipItemCommand(
                actor,
                inventoryItem.ItemInstanceId,
                InventoryEquipmentSlot.OffHand,
                inventoryVersion)));
            if (equip.Accepted)
            {
                builder.CountMessage("InventoryDelta");
            }
            else
            {
                failures.Add($"EquipItemIntent failed: {equip.Status} {equip.Message}");
                builder.CountMessage("ServerError");
            }

            var disconnect = await builder.MeasureAsync("disconnect", () => environment.Movement.DisconnectAsync(character.CharacterId, "disconnect_abrupt", cancellationToken));
            environment.Monsters.DisconnectPlayer(character.CharacterId);
            environment.Sessions.Disconnect(session.ConnectionId, "disconnect_abrupt", preserveLeaseForReconnect: true, disconnect.CombatGraceExpiresAtUtc);

            var reconnectSession = await AuthenticateAsync(environment, $"{botId}-b", builder, cancellationToken);
            builder.CountMessage("ReconnectRequest");
            var reconnect = builder.Measure("reconnect_request", () => environment.Sessions.TryReconnect(
                reconnectSession.ConnectionId,
                joinLease.ReconnectToken.Token,
                session.ConnectionId));
            if (reconnect.Status != ReconnectLeaseStatus.Reconnected)
            {
                failures.Add($"ReconnectRequest failed: {reconnect.Status} {reconnect.Message}");
                builder.CountMessage("ServerError");
            }

            var rejoin = await builder.MeasureAsync("world_rejoin", () => environment.Movement.JoinAsync(character, cancellationToken));
            builder.CountMessage("WorldSnapshot");
            var afterInventory = environment.Inventory.GetOrCreateInventory(actor);
            if (afterInventory.Equipment.SingleOrDefault()?.ItemInstanceId != inventoryItem.ItemInstanceId)
            {
                failures.Add("Equipped item was not preserved after reconnect.");
            }

            if (rejoin.Position != moved.Position)
            {
                failures.Add("Reconnect did not preserve the last movement position.");
            }

            if (saveShutdownCheckpoint)
            {
                await environment.Movement.SaveAllCheckpointsAsync("shutdown", cancellationToken);
            }

            var wallet = rewardDocument.Wallets.SingleOrDefault(wallet => wallet.CharacterId == character.CharacterId);
            return new LoadBotResult(
                botId,
                failures.Count == 0,
                failures,
                duplicateRewards,
                replayRejected,
                reconnect.Status == ReconnectLeaseStatus.Reconnected,
                equip.Accepted,
                rewardGrant?.ItemInstanceIds.Count ?? 0,
                wallet?.Balance ?? 0);
        }
        catch (Exception ex)
        {
            failures.Add($"{ex.GetType().Name}: {ex.Message}");
            return CreateFailed(botId, failures);
        }
    }

    private static void CountCombatResponse(
        LoadBotReportBuilder builder,
        CombatEvent? combatEvent,
        RewardGranted? rewardGranted,
        InventoryDelta? inventoryDelta,
        CharacterProgressed? progressed,
        bool accepted)
    {
        if (combatEvent is not null)
        {
            builder.CountMessage("CombatEvent");
        }

        if (rewardGranted is not null)
        {
            builder.CountMessage("RewardGranted");
        }

        if (inventoryDelta is not null)
        {
            builder.CountMessage("InventoryDelta");
        }

        if (progressed is not null)
        {
            builder.CountMessage("CharacterProgressed");
        }

        if (!accepted)
        {
            builder.CountMessage("ServerError");
        }
    }

    private static int CountDuplicateRewardRecords(RewardTransactionDocument document)
    {
        var duplicateRewardKeys = document.RewardGrants.Count - document.RewardGrants.Select(grant => grant.RewardKey).Distinct(StringComparer.Ordinal).Count();
        var duplicateLedgerRewards = document.CurrencyLedger.Count - document.CurrencyLedger.Select(entry => entry.RewardKey).Distinct(StringComparer.Ordinal).Count();
        var duplicateItemIds = document.ItemInstances.Count - document.ItemInstances.Select(item => item.ItemInstanceId).Distinct(StringComparer.Ordinal).Count();
        return Math.Max(0, duplicateRewardKeys) + Math.Max(0, duplicateLedgerRewards) + Math.Max(0, duplicateItemIds);
    }

    private static LoadBotResult CreateFailed(string botId, IReadOnlyList<string> failures) =>
        new(botId, Success: false, failures, DuplicateRewardRecords: 1, ReplayRejected: false, Reconnected: false, EquippedItem: false, RewardItemCount: 0, CurrencyBalance: 0);

    private static async Task<GatewaySession> AuthenticateAsync(
        LoadBotEnvironment environment,
        string connectionSuffix,
        LoadBotReportBuilder builder,
        CancellationToken cancellationToken)
    {
        var connectionId = "conn-" + connectionSuffix;
        var nonce = "nonce-" + connectionSuffix;
        var issue = await builder.MeasureAsync("game_ticket_issue", () => environment.Tickets.IssueAsync(
            new GameTicketIssueCommand(environment.AccountId, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, nonce),
            cancellationToken));
        if (!issue.Success || string.IsNullOrWhiteSpace(issue.GameTicket))
        {
            throw new InvalidOperationException(issue.Message);
        }

        builder.CountMessage("ClientHello");
        var consumed = await builder.MeasureAsync("client_hello", () => environment.Tickets.ConsumeAsync(
            new GameTicketConsumeCommand(issue.GameTicket!, GameTicketDefaults.DefaultBuildId, ProtocolConstants.SupportedProtocolVersion, nonce),
            cancellationToken));
        if (!consumed.Success || consumed.Ticket is null)
        {
            builder.CountMessage("ServerError");
            throw new InvalidOperationException(consumed.Message);
        }

        return environment.Sessions.CreateAuthenticatedSession(connectionId, consumed.Ticket);
    }

    private static async Task<CharacterRecord> CreateKnightAsync(
        LoadBotEnvironment environment,
        string botId,
        CancellationToken cancellationToken)
    {
        var result = await environment.Characters.CreateKnightAsync(
            new CreateKnightCommand(environment.AccountId, CreateCharacterName(botId)),
            cancellationToken);
        if (!result.Success || result.Character is null)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.Character;
    }

    private static async Task PositionNearFirstMonsterAsync(
        LoadBotEnvironment environment,
        CharacterRecord character,
        CancellationToken cancellationToken)
    {
        var target = environment.Map.MonsterSpawns.First();
        var spawn = new CharacterPosition(target.X - 1, target.Y);
        await environment.CharacterStore.StoreCheckpointAsync(
            new CharacterCheckpointRecord(
                character.CharacterId,
                environment.Map.Map.MapId,
                spawn,
                LastSequence: 0,
                Reason: "load_bot_spawn",
                environment.Time.GetUtcNow(),
                character.Stats.MaxHp,
                character.Stats.MaxMp,
                character.Stats.Level),
            cancellationToken);
    }

    private static string CreateCharacterName(string botId)
    {
        var compact = new string(botId.Where(char.IsLetterOrDigit).Take(16).ToArray());
        return string.IsNullOrWhiteSpace(compact) ? "LoadBot" : "Bot " + compact;
    }
}

internal sealed record BatchReconnectResult(int ExpectedDisconnected, int Disconnected, int Reconnected);

public sealed record LoadBotEnvironment(
    string BotId,
    string AccountId,
    string StorePath,
    ManualLoadBotTimeProvider Time,
    GameTicketService Tickets,
    GatewaySessionManager Sessions,
    FileCharacterStore CharacterStore,
    CharacterService Characters,
    WorldMapCatalog Map,
    WorldMovementRuntime Movement,
    WorldMonsterRuntime Monsters,
    WorldRewardRuntime Rewards,
    InventoryEquipmentService Inventory,
    WorldCombatRuntime Combat) : IDisposable
{
    public static async Task<LoadBotEnvironment> CreateAsync(string botId, CancellationToken cancellationToken)
    {
        var storePath = LoadBotPaths.CreateTempDirectory(botId);
        var time = new ManualLoadBotTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var ticketStore = new FileGameTicketStore(Path.Combine(storePath, "tickets"));
        var tickets = new GameTicketService(ticketStore, time);
        var sessions = new GatewaySessionManager(time, Path.Combine(storePath, "gateway"));
        var characterStore = new FileCharacterStore(Path.Combine(storePath, "characters"));
        var characters = new CharacterService(characterStore, time);
        var map = await WorldMapCatalog.LoadDefaultAsync(cancellationToken);
        var equipment = new WorldEquipmentRuntime();
        var movement = new WorldMovementRuntime(characterStore, map, time, equipment);
        var monsters = new WorldMonsterRuntime(map, time, movement);
        var rewards = new WorldRewardRuntime(Path.Combine(storePath, "rewards"), time, new FixedLoadBotRewardRandom());
        var inventory = new InventoryEquipmentService(Path.Combine(storePath, "inventory"), time);
        var combat = new WorldCombatRuntime(movement, monsters, time, new FixedLoadBotCombatRandom(), rewards);

        return new LoadBotEnvironment(
            botId,
            "account-" + botId,
            storePath,
            time,
            tickets,
            sessions,
            characterStore,
            characters,
            map,
            movement,
            monsters,
            rewards,
            inventory,
            combat);
    }

    public void Dispose() => LoadBotPaths.DeleteDirectory(StorePath);
}

public sealed class FixedLoadBotRewardRandom : IRewardRandomSource
{
    public int NextInclusive(int minValue, int maxValue) => Math.Clamp(2, minValue, maxValue);

    public int NextBasisPoint() => 5;
}

public sealed class FixedLoadBotCombatRandom : ICombatRandomSource
{
    public double NextVariance() => 1.0d;

    public bool RollCritical(decimal criticalChancePercent) => false;
}

public sealed class ManualLoadBotTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualLoadBotTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}

public static class LoadBotPaths
{
    public static string CreateTempDirectory(string name)
    {
        var safeName = new string(name.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        var path = Path.Combine(Path.GetTempPath(), "divinity", "load-bots", safeName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
