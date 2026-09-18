using Divinity.GameRules.Characters;
using Divinity.GameRules.Equipment;

namespace Divinity.GameRules.Inventory;

public enum InventoryEquipmentSlot
{
    Unspecified = 0,
    OffHand = 1
}

public enum InventoryItemLocation
{
    Inventory = 0,
    Equipment = 1,
    PendingReward = 2
}

public enum InventoryOperationStatus
{
    Accepted,
    VersionConflict,
    ItemNotFound,
    InvalidOwner,
    InvalidLocation,
    InvalidClass,
    InvalidLevel,
    InvalidSlot,
    DurabilityZero,
    InventoryFull,
    EquipmentOccupied,
    NothingEquipped,
    PendingRewardNotFound
}

public sealed record InventoryActor(
    string CharacterId,
    string Vocation,
    int Level)
{
    public static InventoryActor FromCharacter(CharacterRecord character) =>
        new(character.CharacterId, character.Vocation, character.Stats.Level);
}

public sealed record InventoryMoveItemCommand(
    InventoryActor Actor,
    string ItemInstanceId,
    uint SourceSlotIndex,
    uint DestinationSlotIndex,
    ulong InventoryVersion);

public sealed record InventoryEquipItemCommand(
    InventoryActor Actor,
    string ItemInstanceId,
    InventoryEquipmentSlot Slot,
    ulong InventoryVersion);

public sealed record InventoryUnequipItemCommand(
    InventoryActor Actor,
    InventoryEquipmentSlot Slot,
    ulong InventoryVersion);

public sealed record InventoryClaimPendingRewardCommand(
    InventoryActor Actor,
    string PendingRewardId,
    ulong InventoryVersion);

public sealed record InventoryOperationResult(
    InventoryOperationStatus Status,
    string Message,
    InventorySnapshot Snapshot,
    InventoryAttributeComparison? AttributeComparison = null)
{
    public bool Accepted => Status == InventoryOperationStatus.Accepted;
}

public sealed record InventorySnapshot(
    string CharacterId,
    ulong InventoryVersion,
    IReadOnlyList<InventorySlotState> Slots,
    IReadOnlyList<EquipmentItemState> Equipment,
    IReadOnlyList<PendingRewardState> PendingRewards,
    int CurrencyBalance,
    int DefenseBonus,
    int BlockChancePercentBonus);

public sealed record InventorySlotState(
    uint SlotIndex,
    string? ItemInstanceId);

public sealed record EquipmentItemState(
    InventoryEquipmentSlot Slot,
    string ItemInstanceId,
    int Durability,
    int MaxDurability,
    bool AttributesActive,
    int DefenseBonus,
    int BlockChancePercentBonus);

public sealed record PendingRewardState(
    string PendingRewardId,
    string ItemInstanceId);

public sealed record InventoryAttributeComparison(
    int CurrentDefense,
    int CandidateDefense,
    int DefenseDelta,
    int CurrentBlockChancePercent,
    int CandidateBlockChancePercent,
    int BlockChanceDeltaPercent);

public sealed record InventoryItemInstanceState(
    string ItemInstanceId,
    string OwnerCharacterId,
    string TemplateId,
    EquipmentRarity Rarity,
    int RequiredLevel,
    string ClassRestriction,
    InventoryEquipmentSlot EquipmentSlot,
    int Durability,
    int MaxDurability,
    InventoryItemLocation Location,
    string? BoundCharacterId);

public sealed record InventoryStoreDocument(
    List<CharacterInventoryRecord> Inventories,
    List<InventoryItemRecord> ItemInstances,
    List<InventorySlotRecord> InventorySlots,
    List<EquipmentLoadoutRecord> EquipmentLoadouts,
    List<InventoryPendingRewardRecord> PendingRewards,
    List<InventoryWalletRecord> Wallets);

public sealed record CharacterInventoryRecord(
    string CharacterId,
    ulong InventoryVersion,
    DateTimeOffset UpdatedAtUtc);

public sealed record InventoryItemRecord(
    string ItemInstanceId,
    string OwnerCharacterId,
    string TemplateId,
    EquipmentRarity Rarity,
    int RequiredLevel,
    string ClassRestriction,
    InventoryEquipmentSlot EquipmentSlot,
    int Durability,
    int MaxDurability,
    InventoryItemLocation Location,
    string? BoundCharacterId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record InventorySlotRecord(
    string CharacterId,
    uint SlotIndex,
    string ItemInstanceId);

public sealed record EquipmentLoadoutRecord(
    string CharacterId,
    InventoryEquipmentSlot Slot,
    string ItemInstanceId);

public sealed record InventoryPendingRewardRecord(
    string PendingRewardId,
    string CharacterId,
    string ItemInstanceId,
    DateTimeOffset CreatedAtUtc);

public sealed record InventoryWalletRecord(
    string CharacterId,
    string CurrencyId,
    int Balance,
    DateTimeOffset UpdatedAtUtc);
