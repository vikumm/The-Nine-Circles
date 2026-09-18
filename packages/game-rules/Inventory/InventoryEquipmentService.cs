using System.Text.Json;
using System.Text.Json.Serialization;
using Divinity.GameRules.Equipment;
using Divinity.GameRules.Rewards;

namespace Divinity.GameRules.Inventory;

public sealed class InventoryEquipmentService
{
    public const int InventorySlotCount = 20;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _rootDirectory;
    private readonly string _dataPath;
    private readonly string _lockPath;
    private readonly TimeProvider _timeProvider;

    public InventoryEquipmentService(string rootDirectory, TimeProvider? timeProvider = null)
    {
        _rootDirectory = rootDirectory;
        _dataPath = Path.Combine(_rootDirectory, "inventory-vs017.json");
        _lockPath = Path.Combine(_rootDirectory, "inventory-vs017.lock");
        _timeProvider = timeProvider ?? TimeProvider.System;
        Directory.CreateDirectory(_rootDirectory);
    }

    public static InventoryEquipmentService FromEnvironment(TimeProvider? timeProvider = null)
    {
        var configuredPath = Environment.GetEnvironmentVariable("DIVINITY_INVENTORY_STORE_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Path.GetTempPath(), "divinity", "inventory")
            : configuredPath;

        return new InventoryEquipmentService(root, timeProvider);
    }

    public InventorySnapshot GetOrCreateInventory(InventoryActor actor) =>
        WithDocumentLock(document =>
        {
            EnsureInventory(document, actor.CharacterId);
            return CreateSnapshot(document, actor.CharacterId);
        });

    public InventoryOperationResult MoveItem(InventoryMoveItemCommand command) =>
        WithDocumentLock(document =>
        {
            var failure = ValidateVersion(document, command.Actor.CharacterId, command.InventoryVersion);
            if (failure is not null)
            {
                return failure;
            }

            if (!IsValidSlot(command.SourceSlotIndex) || !IsValidSlot(command.DestinationSlotIndex))
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidSlot, "Inventory slot is outside the 20 slot range.");
            }

            var item = document.ItemInstances.SingleOrDefault(existing => existing.ItemInstanceId == command.ItemInstanceId);
            if (item is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.ItemNotFound, "Item instance does not exist.");
            }

            if (!string.Equals(item.OwnerCharacterId, command.Actor.CharacterId, StringComparison.Ordinal))
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidOwner, "Item owner does not match the character.");
            }

            if (item.Location != InventoryItemLocation.Inventory)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidLocation, "Only inventory items can be moved between slots.");
            }

            var source = document.InventorySlots.SingleOrDefault(slot =>
                slot.CharacterId == command.Actor.CharacterId &&
                slot.SlotIndex == command.SourceSlotIndex &&
                slot.ItemInstanceId == item.ItemInstanceId);
            if (source is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidLocation, "Source slot does not contain the requested item.");
            }

            var destinationOccupied = document.InventorySlots.Any(slot =>
                slot.CharacterId == command.Actor.CharacterId &&
                slot.SlotIndex == command.DestinationSlotIndex &&
                slot.ItemInstanceId != item.ItemInstanceId);
            if (destinationOccupied)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidSlot, "Destination inventory slot is occupied.");
            }

            document.InventorySlots.Remove(source);
            document.InventorySlots.Add(source with { SlotIndex = command.DestinationSlotIndex });
            TouchInventory(document, command.Actor.CharacterId);

            return Accept(document, command.Actor.CharacterId, "Inventory item moved.");
        });

    public InventoryOperationResult EquipItem(InventoryEquipItemCommand command) =>
        WithDocumentLock(document =>
        {
            var failure = ValidateVersion(document, command.Actor.CharacterId, command.InventoryVersion);
            if (failure is not null)
            {
                return failure;
            }

            if (command.Slot != InventoryEquipmentSlot.OffHand)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidSlot, "MMO-VS1 only supports OffHand equipment.");
            }

            var item = document.ItemInstances.SingleOrDefault(existing => existing.ItemInstanceId == command.ItemInstanceId);
            if (item is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.ItemNotFound, "Item instance does not exist.");
            }

            var validation = ValidateEquippableItem(document, command.Actor, item, command.Slot);
            if (validation is not null)
            {
                return validation;
            }

            var occupied = document.EquipmentLoadouts.Any(loadout =>
                loadout.CharacterId == command.Actor.CharacterId &&
                loadout.Slot == command.Slot);
            if (occupied)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.EquipmentOccupied, "OffHand already has an equipped item.");
            }

            var inventorySlot = document.InventorySlots.Single(slot =>
                slot.CharacterId == command.Actor.CharacterId &&
                slot.ItemInstanceId == item.ItemInstanceId);
            document.InventorySlots.Remove(inventorySlot);
            document.EquipmentLoadouts.Add(new EquipmentLoadoutRecord(command.Actor.CharacterId, command.Slot, item.ItemInstanceId));

            UpdateItem(document, item with
            {
                Location = InventoryItemLocation.Equipment,
                BoundCharacterId = command.Actor.CharacterId,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            });
            TouchInventory(document, command.Actor.CharacterId);

            return Accept(
                document,
                command.Actor.CharacterId,
                "Item equipped.",
                CompareOffHandInternal(document, command.Actor.CharacterId, item.ItemInstanceId));
        });

    public InventoryOperationResult UnequipItem(InventoryUnequipItemCommand command) =>
        WithDocumentLock(document =>
        {
            var failure = ValidateVersion(document, command.Actor.CharacterId, command.InventoryVersion);
            if (failure is not null)
            {
                return failure;
            }

            if (command.Slot != InventoryEquipmentSlot.OffHand)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InvalidSlot, "MMO-VS1 only supports OffHand equipment.");
            }

            var loadout = document.EquipmentLoadouts.SingleOrDefault(existing =>
                existing.CharacterId == command.Actor.CharacterId &&
                existing.Slot == command.Slot);
            if (loadout is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.NothingEquipped, "No OffHand item is equipped.");
            }

            var freeSlot = FindFirstFreeSlot(document, command.Actor.CharacterId);
            if (freeSlot is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InventoryFull, "Inventory is full; unequip cannot drop items.");
            }

            var item = document.ItemInstances.Single(existing => existing.ItemInstanceId == loadout.ItemInstanceId);
            document.EquipmentLoadouts.Remove(loadout);
            document.InventorySlots.Add(new InventorySlotRecord(command.Actor.CharacterId, freeSlot.Value, item.ItemInstanceId));
            UpdateItem(document, item with
            {
                Location = InventoryItemLocation.Inventory,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            });
            TouchInventory(document, command.Actor.CharacterId);

            return Accept(document, command.Actor.CharacterId, "Item unequipped.");
        });

    public InventoryOperationResult ClaimPendingReward(InventoryClaimPendingRewardCommand command) =>
        WithDocumentLock(document =>
        {
            var failure = ValidateVersion(document, command.Actor.CharacterId, command.InventoryVersion);
            if (failure is not null)
            {
                return failure;
            }

            var pending = document.PendingRewards.SingleOrDefault(existing =>
                existing.CharacterId == command.Actor.CharacterId &&
                existing.PendingRewardId == command.PendingRewardId);
            if (pending is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.PendingRewardNotFound, "Pending reward does not exist for this character.");
            }

            var freeSlot = FindFirstFreeSlot(document, command.Actor.CharacterId);
            if (freeSlot is null)
            {
                return Reject(document, command.Actor.CharacterId, InventoryOperationStatus.InventoryFull, "Inventory is full; pending reward remains pending.");
            }

            var item = document.ItemInstances.Single(existing => existing.ItemInstanceId == pending.ItemInstanceId);
            document.PendingRewards.Remove(pending);
            document.InventorySlots.Add(new InventorySlotRecord(command.Actor.CharacterId, freeSlot.Value, item.ItemInstanceId));
            UpdateItem(document, item with
            {
                Location = InventoryItemLocation.Inventory,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            });
            TouchInventory(document, command.Actor.CharacterId);

            return Accept(document, command.Actor.CharacterId, "Pending reward claimed.");
        });

    public InventoryAttributeComparison CompareOffHand(InventoryActor actor, string candidateItemInstanceId) =>
        WithDocumentLock(document =>
        {
            EnsureInventory(document, actor.CharacterId);
            var candidate = document.ItemInstances.SingleOrDefault(item => item.ItemInstanceId == candidateItemInstanceId);
            if (candidate is null)
            {
                return new InventoryAttributeComparison(0, 0, 0, 0, 0, 0);
            }

            return CompareOffHandInternal(document, actor.CharacterId, candidate.ItemInstanceId);
        });

    public InventoryItemInstanceState CreateWoodenShieldForTesting(
        InventoryActor actor,
        EquipmentRarity rarity = EquipmentRarity.Normal,
        int? durability = null,
        string? ownerCharacterId = null,
        string classRestriction = "Knight",
        int requiredLevel = 1,
        InventoryEquipmentSlot equipmentSlot = InventoryEquipmentSlot.OffHand,
        uint? targetSlotIndex = null)
    {
        return WithDocumentLock(document =>
        {
            EnsureInventory(document, actor.CharacterId);
            var slot = targetSlotIndex ?? FindFirstFreeSlot(document, actor.CharacterId) ?? throw new InvalidOperationException("Inventory is full.");
            if (!IsValidSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(targetSlotIndex), "Inventory slot is outside the 20 slot range.");
            }

            if (document.InventorySlots.Any(existing => existing.CharacterId == actor.CharacterId && existing.SlotIndex == slot))
            {
                throw new InvalidOperationException("Inventory slot is occupied.");
            }

            var now = _timeProvider.GetUtcNow();
            var item = new InventoryItemRecord(
                CreateItemInstanceId(),
                ownerCharacterId ?? actor.CharacterId,
                WoodenShieldCatalog.TemplateId,
                rarity,
                requiredLevel,
                classRestriction,
                equipmentSlot,
                durability ?? WoodenShieldCatalog.MaxDurability,
                WoodenShieldCatalog.MaxDurability,
                InventoryItemLocation.Inventory,
                null,
                now,
                now);

            document.ItemInstances.Add(item);
            document.InventorySlots.Add(new InventorySlotRecord(actor.CharacterId, slot, item.ItemInstanceId));
            TouchInventory(document, actor.CharacterId);
            return ToItemState(item);
        });
    }

    public PendingRewardState CreatePendingWoodenShieldForTesting(
        InventoryActor actor,
        EquipmentRarity rarity = EquipmentRarity.Normal,
        int currencyBalance = 0)
    {
        return WithDocumentLock(document =>
        {
            EnsureInventory(document, actor.CharacterId);
            UpsertWallet(document, actor.CharacterId, MossSlimeRewardCatalog.CurrencyId, currencyBalance);
            var now = _timeProvider.GetUtcNow();
            var item = new InventoryItemRecord(
                CreateItemInstanceId(),
                actor.CharacterId,
                WoodenShieldCatalog.TemplateId,
                rarity,
                1,
                "Knight",
                InventoryEquipmentSlot.OffHand,
                WoodenShieldCatalog.MaxDurability,
                WoodenShieldCatalog.MaxDurability,
                InventoryItemLocation.PendingReward,
                null,
                now,
                now);
            var pending = new InventoryPendingRewardRecord(
                "pending_" + Guid.CreateVersion7().ToString("N"),
                actor.CharacterId,
                item.ItemInstanceId,
                now);

            document.ItemInstances.Add(item);
            document.PendingRewards.Add(pending);
            TouchInventory(document, actor.CharacterId);
            return new PendingRewardState(pending.PendingRewardId, pending.ItemInstanceId);
        });
    }

    public void FillInventoryForTesting(InventoryActor actor)
    {
        WithDocumentLock(document =>
        {
            EnsureInventory(document, actor.CharacterId);
            for (uint slot = 0; slot < InventorySlotCount; slot++)
            {
                if (document.InventorySlots.Any(existing => existing.CharacterId == actor.CharacterId && existing.SlotIndex == slot))
                {
                    continue;
                }

                var now = _timeProvider.GetUtcNow();
                var item = new InventoryItemRecord(
                    CreateItemInstanceId(),
                    actor.CharacterId,
                    "test_filler_item_t0",
                    EquipmentRarity.Normal,
                    1,
                    "Knight",
                    InventoryEquipmentSlot.Unspecified,
                    0,
                    0,
                    InventoryItemLocation.Inventory,
                    null,
                    now,
                    now);
                document.ItemInstances.Add(item);
                document.InventorySlots.Add(new InventorySlotRecord(actor.CharacterId, slot, item.ItemInstanceId));
            }

            TouchInventory(document, actor.CharacterId);
            return true;
        });
    }

    public InventoryStoreDocument ReadDocumentForTesting() =>
        WithDocumentLock(document => document);

    private InventoryOperationResult? ValidateVersion(InventoryStoreDocument document, string characterId, ulong inventoryVersion)
    {
        EnsureInventory(document, characterId);
        var current = document.Inventories.Single(inventory => inventory.CharacterId == characterId);
        return current.InventoryVersion == inventoryVersion
            ? null
            : Reject(document, characterId, InventoryOperationStatus.VersionConflict, "Inventory version conflict; refresh before mutating inventory.");
    }

    private InventoryOperationResult? ValidateEquippableItem(
        InventoryStoreDocument document,
        InventoryActor actor,
        InventoryItemRecord item,
        InventoryEquipmentSlot requestedSlot)
    {
        if (!string.Equals(item.OwnerCharacterId, actor.CharacterId, StringComparison.Ordinal))
        {
            return Reject(document, actor.CharacterId, InventoryOperationStatus.InvalidOwner, "Item owner does not match the character.");
        }

        if (item.Location != InventoryItemLocation.Inventory ||
            !document.InventorySlots.Any(slot => slot.CharacterId == actor.CharacterId && slot.ItemInstanceId == item.ItemInstanceId))
        {
            return Reject(document, actor.CharacterId, InventoryOperationStatus.InvalidLocation, "Only inventory items can be equipped.");
        }

        if (!string.IsNullOrWhiteSpace(item.BoundCharacterId) &&
            !string.Equals(item.BoundCharacterId, actor.CharacterId, StringComparison.Ordinal))
        {
            return Reject(document, actor.CharacterId, InventoryOperationStatus.InvalidOwner, "Item is bound to another character.");
        }

        if (!string.Equals(item.ClassRestriction, actor.Vocation, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(document, actor.CharacterId, InventoryOperationStatus.InvalidClass, "Item class restriction does not match the character vocation.");
        }

        if (actor.Level < item.RequiredLevel)
        {
            return Reject(document, actor.CharacterId, InventoryOperationStatus.InvalidLevel, "Character level is below the item minimum level.");
        }

        if (item.EquipmentSlot != requestedSlot || item.EquipmentSlot != InventoryEquipmentSlot.OffHand)
        {
            return Reject(document, actor.CharacterId, InventoryOperationStatus.InvalidSlot, "Item cannot be equipped in the requested slot.");
        }

        return null;
    }

    private InventoryOperationResult Accept(
        InventoryStoreDocument document,
        string characterId,
        string message,
        InventoryAttributeComparison? comparison = null) =>
        new(InventoryOperationStatus.Accepted, message, CreateSnapshot(document, characterId), comparison);

    private InventoryOperationResult Reject(
        InventoryStoreDocument document,
        string characterId,
        InventoryOperationStatus status,
        string message) =>
        new(status, message, CreateSnapshot(document, characterId));

    private static InventoryAttributeComparison CompareOffHandInternal(
        InventoryStoreDocument document,
        string characterId,
        string candidateItemInstanceId)
    {
        var currentLoadout = document.EquipmentLoadouts.SingleOrDefault(loadout =>
            loadout.CharacterId == characterId &&
            loadout.Slot == InventoryEquipmentSlot.OffHand);
        var currentItem = currentLoadout is null
            ? null
            : document.ItemInstances.SingleOrDefault(item => item.ItemInstanceId == currentLoadout.ItemInstanceId);
        var candidateItem = document.ItemInstances.Single(item => item.ItemInstanceId == candidateItemInstanceId);

        var current = currentItem is null ? new EquipmentAttributeBonus(0, 0) : GetActiveBonus(currentItem);
        var candidate = GetActiveBonus(candidateItem);
        return new InventoryAttributeComparison(
            current.Defense,
            candidate.Defense,
            candidate.Defense - current.Defense,
            current.Block,
            candidate.Block,
            candidate.Block - current.Block);
    }

    private static InventorySnapshot CreateSnapshot(InventoryStoreDocument document, string characterId)
    {
        EnsureInventory(document, characterId);

        var slots = new List<InventorySlotState>(InventorySlotCount);
        for (uint slot = 0; slot < InventorySlotCount; slot++)
        {
            var slotRecord = document.InventorySlots.SingleOrDefault(existing =>
                existing.CharacterId == characterId &&
                existing.SlotIndex == slot);
            slots.Add(new InventorySlotState(slot, slotRecord?.ItemInstanceId));
        }

        var equipment = document.EquipmentLoadouts
            .Where(loadout => loadout.CharacterId == characterId)
            .OrderBy(loadout => loadout.Slot)
            .Select(loadout =>
            {
                var item = document.ItemInstances.Single(existing => existing.ItemInstanceId == loadout.ItemInstanceId);
                var bonus = GetActiveBonus(item);
                return new EquipmentItemState(
                    loadout.Slot,
                    item.ItemInstanceId,
                    item.Durability,
                    item.MaxDurability,
                    item.Durability > 0,
                    bonus.Defense,
                    bonus.Block);
            })
            .ToArray();

        var defense = equipment.Sum(item => item.DefenseBonus);
        var block = equipment.Sum(item => item.BlockChancePercentBonus);
        var wallet = document.Wallets.SingleOrDefault(existing =>
            existing.CharacterId == characterId &&
            existing.CurrencyId == MossSlimeRewardCatalog.CurrencyId);
        var inventory = document.Inventories.Single(existing => existing.CharacterId == characterId);

        return new InventorySnapshot(
            characterId,
            inventory.InventoryVersion,
            slots,
            equipment,
            document.PendingRewards
                .Where(pending => pending.CharacterId == characterId)
                .OrderBy(pending => pending.CreatedAtUtc)
                .Select(pending => new PendingRewardState(pending.PendingRewardId, pending.ItemInstanceId))
                .ToArray(),
            wallet?.Balance ?? 0,
            defense,
            block);
    }

    private static EquipmentAttributeBonus GetActiveBonus(InventoryItemRecord item) =>
        string.Equals(item.TemplateId, WoodenShieldCatalog.TemplateId, StringComparison.Ordinal)
            ? WoodenShieldCatalog.GetActiveAttributes(item.Rarity, item.Durability)
            : new EquipmentAttributeBonus(0, 0);

    private static uint? FindFirstFreeSlot(InventoryStoreDocument document, string characterId)
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

    private static bool IsValidSlot(uint slot) => slot < InventorySlotCount;

    private void TouchInventory(InventoryStoreDocument document, string characterId)
    {
        EnsureInventory(document, characterId);
        var index = document.Inventories.FindIndex(inventory => inventory.CharacterId == characterId);
        var current = document.Inventories[index];
        document.Inventories[index] = current with
        {
            InventoryVersion = current.InventoryVersion + 1,
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };
    }

    private static void EnsureInventory(InventoryStoreDocument document, string characterId)
    {
        if (document.Inventories.Any(inventory => inventory.CharacterId == characterId))
        {
            return;
        }

        document.Inventories.Add(new CharacterInventoryRecord(characterId, 1, DateTimeOffset.UnixEpoch));
    }

    private static void UpdateItem(InventoryStoreDocument document, InventoryItemRecord updated)
    {
        var index = document.ItemInstances.FindIndex(item => item.ItemInstanceId == updated.ItemInstanceId);
        document.ItemInstances[index] = updated;
    }

    private void UpsertWallet(InventoryStoreDocument document, string characterId, string currencyId, int balance)
    {
        var index = document.Wallets.FindIndex(wallet =>
            wallet.CharacterId == characterId &&
            wallet.CurrencyId == currencyId);
        var now = _timeProvider.GetUtcNow();
        if (index < 0)
        {
            document.Wallets.Add(new InventoryWalletRecord(characterId, currencyId, balance, now));
            return;
        }

        document.Wallets[index] = document.Wallets[index] with
        {
            Balance = balance,
            UpdatedAtUtc = now
        };
    }

    private TResult WithDocumentLock<TResult>(Func<InventoryStoreDocument, TResult> action)
    {
        Directory.CreateDirectory(_rootDirectory);
        using var fileLock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var document = ReadDocument();
        var result = action(document);
        WriteDocument(document);
        return result;
    }

    private InventoryStoreDocument ReadDocument()
    {
        if (!File.Exists(_dataPath))
        {
            return new InventoryStoreDocument([], [], [], [], [], []);
        }

        var json = File.ReadAllText(_dataPath);
        return JsonSerializer.Deserialize<InventoryStoreDocument>(json, JsonOptions)
            ?? new InventoryStoreDocument([], [], [], [], [], []);
    }

    private void WriteDocument(InventoryStoreDocument document)
    {
        var tempPath = _dataPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(tempPath, _dataPath, overwrite: true);
    }

    private static InventoryItemInstanceState ToItemState(InventoryItemRecord item) =>
        new(
            item.ItemInstanceId,
            item.OwnerCharacterId,
            item.TemplateId,
            item.Rarity,
            item.RequiredLevel,
            item.ClassRestriction,
            item.EquipmentSlot,
            item.Durability,
            item.MaxDurability,
            item.Location,
            item.BoundCharacterId);

    private static string CreateItemInstanceId() =>
        "item_" + Guid.CreateVersion7().ToString("N");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
