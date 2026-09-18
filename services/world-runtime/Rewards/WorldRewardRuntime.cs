using System.Text.Json;
using Divinity.Contracts.V1;
using Divinity.GameRules.Rewards;
using Divinity.WorldRuntime.Observability;

namespace Divinity.WorldRuntime.Rewards;

public sealed class WorldRewardRuntime
{
    public const int InventorySlotCount = 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _rootDirectory;
    private readonly string _dataPath;
    private readonly string _lockPath;
    private readonly TimeProvider _timeProvider;
    private readonly IRewardRandomSource _random;
    private int _transientFailuresToInject;
    private int _deadlockFailuresToInject;

    public WorldRewardRuntime(
        string rootDirectory,
        TimeProvider? timeProvider = null,
        IRewardRandomSource? random = null)
    {
        _rootDirectory = rootDirectory;
        _dataPath = Path.Combine(_rootDirectory, "rewards-vs016.json");
        _lockPath = Path.Combine(_rootDirectory, "rewards-vs016.lock");
        _timeProvider = timeProvider ?? TimeProvider.System;
        _random = random ?? new SystemRewardRandomSource();
        Directory.CreateDirectory(_rootDirectory);
    }

    public static WorldRewardRuntime FromEnvironment(TimeProvider? timeProvider = null, IRewardRandomSource? random = null)
    {
        var configuredPath = Environment.GetEnvironmentVariable("DIVINITY_REWARD_STORE_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Path.GetTempPath(), "divinity", "rewards")
            : configuredPath;

        return new WorldRewardRuntime(root, timeProvider, random);
    }

    public WorldRewardGrantResult GrantMossSlimeReward(string killId, string characterId, string skillId = "", int skillXp = 0) =>
        GrantReward(new WorldRewardGrantCommand(killId, characterId, MossSlimeRewardCatalog.LootTableId, skillId, skillXp));

    public WorldRewardGrantResult GrantReward(WorldRewardGrantCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.KillId) || string.IsNullOrWhiteSpace(command.CharacterId))
        {
            var invalid = CreateInvalidResult(command, "kill_id and character_id are required for reward grants.");
            WorldRuntimeTelemetry.RecordReward(invalid.Status.ToString());
            return invalid;
        }

        if (!string.Equals(command.LootTableId, MossSlimeRewardCatalog.LootTableId, StringComparison.Ordinal))
        {
            var invalid = CreateInvalidResult(command, "Unsupported loot table for MMO-VS1 reward grant.");
            WorldRuntimeTelemetry.RecordReward(invalid.Status.ToString());
            return invalid;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var result = CommitReward(command);
                WorldRuntimeTelemetry.RecordReward(result.Status.ToString());
                return result;
            }
            catch (RewardRetryableException) when (attempt < 3)
            {
            }
        }

        try
        {
            var result = CommitReward(command);
            WorldRuntimeTelemetry.RecordReward(result.Status.ToString());
            return result;
        }
        catch (RewardRetryableException ex)
        {
            var key = CreateRewardKey(command.KillId, command.CharacterId);
            var failed = new WorldRewardGrantResult(
                WorldRewardGrantStatus.Failed,
                ex.Message,
                key,
                new RewardGranted { RewardKey = key, CharacterId = command.CharacterId },
                new InventoryDelta(),
                null,
                Idempotent: false,
                ItemPending: false);
            WorldRuntimeTelemetry.RecordReward(failed.Status.ToString());
            return failed;
        }
    }

    public void InjectFailureForTesting(WorldRewardFailureKind kind, int count = 1)
    {
        if (kind == WorldRewardFailureKind.Deadlock)
        {
            _deadlockFailuresToInject = Math.Max(0, count);
            return;
        }

        _transientFailuresToInject = Math.Max(0, count);
    }

    public void FillInventoryForTesting(string characterId)
    {
        WithDocumentLock(document =>
        {
            for (uint slot = 0; slot < InventorySlotCount; slot++)
            {
                if (document.InventorySlots.Any(existing => existing.CharacterId == characterId && existing.SlotIndex == slot))
                {
                    continue;
                }

                var itemInstanceId = CreateItemInstanceId();
                document.ItemInstances.Add(new ItemInstanceRecord(
                    itemInstanceId,
                    characterId,
                    "test_filler_item_t0",
                    MossSlimeRewardCatalog.NormalRarity,
                    Durability: 0,
                    MaxDurability: 0,
                    Location: "inventory",
                    _timeProvider.GetUtcNow()));
                document.InventorySlots.Add(new InventorySlotRecord(characterId, slot, itemInstanceId));
            }

            return true;
        });
    }

    public RewardTransactionDocument ReadDocumentForTesting() =>
        WithDocumentLock(document => document);

    private WorldRewardGrantResult CommitReward(WorldRewardGrantCommand command) =>
        WithDocumentLock(document =>
        {
            var now = _timeProvider.GetUtcNow();
            var rewardKey = CreateRewardKey(command.KillId, command.CharacterId);
            var existing = document.RewardGrants.FirstOrDefault(grant => grant.RewardKey == rewardKey);
            if (existing is not null)
            {
                return CreateResultFromExisting(document, existing, idempotent: true);
            }

            InjectRetryableFailuresIfRequested();

            var loot = MossSlimeRewardCatalog.Roll(_random);
            var currencyDelta = loot.CurrencyAmount;
            var wallet = UpsertWallet(document, command.CharacterId, MossSlimeRewardCatalog.CurrencyId, currencyDelta, now);
            var itemInstanceIds = new List<string>();
            var inventoryDelta = new InventoryDelta
            {
                InventoryVersion = (ulong)(document.InventorySlots.Count(slot => slot.CharacterId == command.CharacterId) + document.PendingRewards.Count(pending => pending.CharacterId == command.CharacterId) + 1),
                CurrencyBalance = (uint)wallet.Balance
            };

            var itemPending = false;
            if (loot.ItemDrop is not null)
            {
                var item = CreateItemInstance(command.CharacterId, loot.ItemDrop, now);
                itemInstanceIds.Add(item.ItemInstanceId);
                var freeSlot = FindFirstFreeSlot(document, command.CharacterId);
                if (freeSlot is null)
                {
                    itemPending = true;
                    item = item with { Location = "pending_reward" };
                    document.PendingRewards.Add(new PendingRewardRecord(
                        "pending_" + Guid.CreateVersion7().ToString("N"),
                        rewardKey,
                        command.CharacterId,
                        item.ItemInstanceId,
                        now));
                }
                else
                {
                    item = item with { Location = "inventory" };
                    document.InventorySlots.Add(new InventorySlotRecord(command.CharacterId, freeSlot.Value, item.ItemInstanceId));
                    inventoryDelta.Slots.Add(new InventorySlot
                    {
                        SlotIndex = freeSlot.Value,
                        ItemInstanceId = item.ItemInstanceId
                    });
                }

                document.ItemInstances.Add(item);
            }

            var xp = (uint)MossSlimeRewardCatalog.KillXp;
            var skillXp = (uint)Math.Max(0, command.SkillXp);
            var progress = UpsertProgress(document, command.CharacterId, xp, skillXp, now);
            var grant = new RewardGrantRecord(
                rewardKey,
                command.KillId,
                command.CharacterId,
                xp,
                skillXp,
                (uint)currencyDelta,
                itemInstanceIds,
                now);

            document.RewardGrants.Add(grant);
            document.CurrencyLedger.Add(new CurrencyLedgerRecord(
                "ledger_" + Guid.CreateVersion7().ToString("N"),
                rewardKey,
                command.CharacterId,
                MossSlimeRewardCatalog.CurrencyId,
                currencyDelta,
                wallet.Balance,
                now));
            document.OutboxEvents.Add(new OutboxEventRecord(
                "outbox_" + Guid.CreateVersion7().ToString("N"),
                "reward.granted.v1",
                rewardKey,
                now));
            document.AuditEvents.Add(new RewardAuditEventRecord(
                "audit_" + Guid.CreateVersion7().ToString("N"),
                "reward_grant_committed",
                rewardKey,
                command.CharacterId,
                command.KillId,
                now));

            return CreateResult(grant, inventoryDelta, progress, command.SkillId, idempotent: false, itemPending);
        });

    private void InjectRetryableFailuresIfRequested()
    {
        if (_deadlockFailuresToInject > 0)
        {
            _deadlockFailuresToInject--;
            throw new RewardRetryableException("Injected reward deadlock; retrying transaction safely.");
        }

        if (_transientFailuresToInject > 0)
        {
            _transientFailuresToInject--;
            throw new RewardRetryableException("Injected transient reward failure; retrying transaction safely.");
        }
    }

    private static WalletRecord UpsertWallet(
        RewardTransactionDocument document,
        string characterId,
        string currencyId,
        int delta,
        DateTimeOffset now)
    {
        var index = document.Wallets.FindIndex(wallet => wallet.CharacterId == characterId && wallet.CurrencyId == currencyId);
        if (index < 0)
        {
            var created = new WalletRecord(characterId, currencyId, delta, now);
            document.Wallets.Add(created);
            return created;
        }

        var current = document.Wallets[index];
        var updated = current with
        {
            Balance = current.Balance + delta,
            UpdatedAtUtc = now
        };
        document.Wallets[index] = updated;
        return updated;
    }

    private static CharacterRewardProgressRecord UpsertProgress(
        RewardTransactionDocument document,
        string characterId,
        uint xp,
        uint skillXp,
        DateTimeOffset now)
    {
        var index = document.CharacterProgress.FindIndex(progress => progress.CharacterId == characterId);
        if (index < 0)
        {
            var created = new CharacterRewardProgressRecord(characterId, xp, skillXp, now);
            document.CharacterProgress.Add(created);
            return created;
        }

        var current = document.CharacterProgress[index];
        var updated = current with
        {
            Xp = current.Xp + xp,
            SkillXp = current.SkillXp + skillXp,
            UpdatedAtUtc = now
        };
        document.CharacterProgress[index] = updated;
        return updated;
    }

    private static ItemInstanceRecord CreateItemInstance(
        string characterId,
        RewardItemDrop item,
        DateTimeOffset now) =>
        new(
            CreateItemInstanceId(),
            characterId,
            item.ItemTemplateId,
            item.Rarity,
            item.Durable ? item.MaxDurability : 0,
            item.MaxDurability,
            "unassigned",
            now);

    private static uint? FindFirstFreeSlot(RewardTransactionDocument document, string characterId)
    {
        var occupied = document.InventorySlots
            .Where(slot => slot.CharacterId == characterId)
            .Select(slot => slot.SlotIndex)
            .ToHashSet();

        for (uint slot = 0; slot < InventorySlotCount; slot++)
        {
            if (!occupied.Contains(slot))
            {
                return slot;
            }
        }

        return null;
    }

    private static WorldRewardGrantResult CreateResultFromExisting(
        RewardTransactionDocument document,
        RewardGrantRecord grant,
        bool idempotent)
    {
        var wallet = document.Wallets.FirstOrDefault(existing => existing.CharacterId == grant.CharacterId && existing.CurrencyId == MossSlimeRewardCatalog.CurrencyId);
        var progress = document.CharacterProgress.FirstOrDefault(existing => existing.CharacterId == grant.CharacterId);
        var inventoryDelta = new InventoryDelta
        {
            InventoryVersion = (ulong)(document.InventorySlots.Count(slot => slot.CharacterId == grant.CharacterId) + document.PendingRewards.Count(pending => pending.CharacterId == grant.CharacterId)),
            CurrencyBalance = (uint)Math.Max(0, wallet?.Balance ?? 0)
        };

        foreach (var slot in document.InventorySlots.Where(slot => slot.CharacterId == grant.CharacterId && grant.ItemInstanceIds.Contains(slot.ItemInstanceId)))
        {
            inventoryDelta.Slots.Add(new InventorySlot
            {
                SlotIndex = slot.SlotIndex,
                ItemInstanceId = slot.ItemInstanceId
            });
        }

        return CreateResult(
            grant,
            inventoryDelta,
            progress,
            skillId: string.Empty,
            idempotent,
            itemPending: document.PendingRewards.Any(pending => pending.RewardKey == grant.RewardKey));
    }

    private static WorldRewardGrantResult CreateResult(
        RewardGrantRecord grant,
        InventoryDelta inventoryDelta,
        CharacterRewardProgressRecord? progress,
        string skillId,
        bool idempotent,
        bool itemPending)
    {
        var rewardGranted = new RewardGranted
        {
            RewardKey = grant.RewardKey,
            CharacterId = grant.CharacterId,
            Xp = grant.Xp,
            SkillXp = grant.SkillXp,
            CurrencyDelta = grant.CurrencyDelta
        };
        rewardGranted.ItemInstanceIds.AddRange(grant.ItemInstanceIds);

        CharacterProgressed? characterProgressed = progress is null
            ? null
            : new CharacterProgressed
            {
                CharacterId = progress.CharacterId,
                SkillId = string.IsNullOrWhiteSpace(skillId) ? "reward_kill" : skillId,
                SkillXp = progress.SkillXp,
                SkillRank = 1,
                MaxRank = 1
            };

        return new WorldRewardGrantResult(
            idempotent ? WorldRewardGrantStatus.AlreadyGranted : WorldRewardGrantStatus.Granted,
            idempotent ? "Reward grant already exists; returning idempotent result." : "Reward grant committed.",
            grant.RewardKey,
            rewardGranted,
            inventoryDelta,
            characterProgressed,
            idempotent,
            itemPending);
    }

    private static WorldRewardGrantResult CreateInvalidResult(WorldRewardGrantCommand command, string message)
    {
        var rewardKey = CreateRewardKey(command.KillId, command.CharacterId);
        return new WorldRewardGrantResult(
            WorldRewardGrantStatus.InvalidCommand,
            message,
            rewardKey,
            new RewardGranted { RewardKey = rewardKey, CharacterId = command.CharacterId },
            new InventoryDelta(),
            null,
            Idempotent: false,
            ItemPending: false);
    }

    private T WithDocumentLock<T>(Func<RewardTransactionDocument, T> operation)
    {
        Directory.CreateDirectory(_rootDirectory);
        using var lockStream = OpenLock();
        var document = ReadDocument();
        var result = operation(document);
        WriteDocument(document);
        return result;
    }

    private FileStream OpenLock()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 99)
            {
                Thread.Sleep(10);
            }
        }

        return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private RewardTransactionDocument ReadDocument()
    {
        if (!File.Exists(_dataPath))
        {
            return EmptyDocument();
        }

        using var stream = new FileStream(_dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = JsonSerializer.Deserialize<RewardTransactionDocument>(stream, JsonOptions)
            ?? EmptyDocument();

        return NormalizeDocument(document);
    }

    private void WriteDocument(RewardTransactionDocument document)
    {
        var tempPath = _dataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, document, JsonOptions);
            stream.Write("\n"u8);
        }

        File.Move(tempPath, _dataPath, overwrite: true);
    }

    private static RewardTransactionDocument EmptyDocument() => new([], [], [], [], [], [], [], [], []);

    private static RewardTransactionDocument NormalizeDocument(RewardTransactionDocument document) =>
        new(
            document.RewardGrants ?? [],
            document.CurrencyLedger ?? [],
            document.Wallets ?? [],
            document.ItemInstances ?? [],
            document.InventorySlots ?? [],
            document.PendingRewards ?? [],
            document.CharacterProgress ?? [],
            document.OutboxEvents ?? [],
            document.AuditEvents ?? []);

    private static string CreateRewardKey(string killId, string characterId) =>
        $"{killId}:{characterId}";

    private static string CreateItemInstanceId() =>
        "item_" + Guid.CreateVersion7().ToString("N");

    private sealed class RewardRetryableException : Exception
    {
        public RewardRetryableException(string message)
            : base(message)
        {
        }
    }
}
