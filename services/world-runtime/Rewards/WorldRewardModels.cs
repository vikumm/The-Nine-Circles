using Divinity.Contracts.V1;

namespace Divinity.WorldRuntime.Rewards;

public sealed record WorldRewardGrantCommand(
    string KillId,
    string CharacterId,
    string LootTableId,
    string SkillId,
    int SkillXp);

public sealed record WorldRewardGrantResult(
    WorldRewardGrantStatus Status,
    string Message,
    string RewardKey,
    RewardGranted RewardGranted,
    InventoryDelta InventoryDelta,
    CharacterProgressed? CharacterProgressed,
    bool Idempotent,
    bool ItemPending);

public enum WorldRewardGrantStatus
{
    Granted,
    AlreadyGranted,
    InvalidCommand,
    Failed
}

public enum WorldRewardFailureKind
{
    Transient,
    Deadlock
}

public sealed record RewardTransactionDocument(
    List<RewardGrantRecord> RewardGrants,
    List<CurrencyLedgerRecord> CurrencyLedger,
    List<WalletRecord> Wallets,
    List<ItemInstanceRecord> ItemInstances,
    List<InventorySlotRecord> InventorySlots,
    List<PendingRewardRecord> PendingRewards,
    List<CharacterRewardProgressRecord> CharacterProgress,
    List<OutboxEventRecord> OutboxEvents,
    List<RewardAuditEventRecord> AuditEvents);

public sealed record RewardGrantRecord(
    string RewardKey,
    string KillId,
    string CharacterId,
    uint Xp,
    uint SkillXp,
    uint CurrencyDelta,
    List<string> ItemInstanceIds,
    DateTimeOffset CreatedAtUtc);

public sealed record CurrencyLedgerRecord(
    string LedgerId,
    string RewardKey,
    string CharacterId,
    string CurrencyId,
    int Delta,
    int BalanceAfter,
    DateTimeOffset CreatedAtUtc);

public sealed record WalletRecord(
    string CharacterId,
    string CurrencyId,
    int Balance,
    DateTimeOffset UpdatedAtUtc);

public sealed record ItemInstanceRecord(
    string ItemInstanceId,
    string OwnerCharacterId,
    string TemplateId,
    string Rarity,
    int Durability,
    int MaxDurability,
    string Location,
    DateTimeOffset CreatedAtUtc);

public sealed record InventorySlotRecord(
    string CharacterId,
    uint SlotIndex,
    string ItemInstanceId);

public sealed record PendingRewardRecord(
    string PendingRewardId,
    string RewardKey,
    string CharacterId,
    string ItemInstanceId,
    DateTimeOffset CreatedAtUtc);

public sealed record CharacterRewardProgressRecord(
    string CharacterId,
    uint Xp,
    uint SkillXp,
    DateTimeOffset UpdatedAtUtc);

public sealed record OutboxEventRecord(
    string EventId,
    string EventType,
    string RewardKey,
    DateTimeOffset ReadyAtUtc);

public sealed record RewardAuditEventRecord(
    string AuditId,
    string EventType,
    string RewardKey,
    string CharacterId,
    string KillId,
    DateTimeOffset CreatedAtUtc);
