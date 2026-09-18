using Divinity.GameRules.Equipment;
using Divinity.GameRules.Inventory;

var checks = new List<InventoryEquipmentCheck>
{
    Check("initial inventory has 20 slots", InitialInventoryHasTwentySlots()),
    Check("valid item move uses optimistic version", ValidMoveUsesOptimisticVersion()),
    Check("version conflict rejects mutation", VersionConflictRejectsMutation()),
    Check("valid OffHand equip binds item and removes inventory slot", ValidEquipBindsAndRemovesSlot()),
    Check("equip missing item fails", MissingItemFails()),
    Check("equip item owned by another character fails", OtherOwnerFails()),
    Check("equip invalid class fails", InvalidClassFails()),
    Check("equip invalid slot fails", InvalidSlotFails()),
    Check("equip invalid level fails", InvalidLevelFails()),
    Check("valid unequip returns item to first free slot", ValidUnequipReturnsToInventory()),
    Check("unequip with full inventory fails without item loss", UnequipFullInventoryFails()),
    Check("zero durability equipment grants no attributes", ZeroDurabilityHasNoAttributes()),
    Check("pending reward can be claimed into inventory", PendingRewardClaimed()),
    Check("defense comparison reports gain and loss", DefenseComparisonReportsDelta())
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-017 inventory/equipment tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-017 inventory/equipment tests passed.");
return 0;

static bool InitialInventoryHasTwentySlots()
{
    using var fixture = InventoryFixture.Create();
    var snapshot = fixture.Inventory.GetOrCreateInventory(fixture.Actor);

    return snapshot.Slots.Count == InventoryEquipmentService.InventorySlotCount
        && snapshot.Slots.All(slot => slot.ItemInstanceId is null)
        && snapshot.InventoryVersion == 1;
}

static bool ValidMoveUsesOptimisticVersion()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, targetSlotIndex: 0);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;

    var result = fixture.Inventory.MoveItem(new InventoryMoveItemCommand(fixture.Actor, item.ItemInstanceId, 0, 5, version));

    return result.Accepted
        && result.Snapshot.Slots.Single(slot => slot.SlotIndex == 0).ItemInstanceId is null
        && result.Snapshot.Slots.Single(slot => slot.SlotIndex == 5).ItemInstanceId == item.ItemInstanceId
        && result.Snapshot.InventoryVersion == version + 1;
}

static bool VersionConflictRejectsMutation()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, targetSlotIndex: 0);
    var snapshot = fixture.Inventory.GetOrCreateInventory(fixture.Actor);

    var result = fixture.Inventory.MoveItem(new InventoryMoveItemCommand(fixture.Actor, item.ItemInstanceId, 0, 4, snapshot.InventoryVersion - 1));
    var after = fixture.Inventory.GetOrCreateInventory(fixture.Actor);

    return result.Status == InventoryOperationStatus.VersionConflict
        && after.Slots.Single(slot => slot.SlotIndex == 0).ItemInstanceId == item.ItemInstanceId
        && after.Slots.Single(slot => slot.SlotIndex == 4).ItemInstanceId is null
        && after.InventoryVersion == snapshot.InventoryVersion;
}

static bool ValidEquipBindsAndRemovesSlot()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, EquipmentRarity.Good, targetSlotIndex: 2);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;

    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));
    var stored = fixture.Inventory.ReadDocumentForTesting().ItemInstances.Single(existing => existing.ItemInstanceId == item.ItemInstanceId);

    return result.Accepted
        && result.Snapshot.Slots.Single(slot => slot.SlotIndex == 2).ItemInstanceId is null
        && result.Snapshot.Equipment.Single().ItemInstanceId == item.ItemInstanceId
        && result.Snapshot.Equipment.Single().DefenseBonus == 4
        && result.Snapshot.Equipment.Single().BlockChancePercentBonus == 1
        && stored.BoundCharacterId == fixture.Actor.CharacterId
        && stored.Location == InventoryItemLocation.Equipment;
}

static bool MissingItemFails()
{
    using var fixture = InventoryFixture.Create();
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, "item_missing", InventoryEquipmentSlot.OffHand, version));

    return result.Status == InventoryOperationStatus.ItemNotFound
        && result.Snapshot.Equipment.Count == 0;
}

static bool OtherOwnerFails()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, ownerCharacterId: "character-other-owner");
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));

    return result.Status == InventoryOperationStatus.InvalidOwner
        && result.Snapshot.Equipment.Count == 0
        && result.Snapshot.Slots.Count(slot => slot.ItemInstanceId == item.ItemInstanceId) == 1;
}

static bool InvalidClassFails()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, classRestriction: "Mage");
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));

    return result.Status == InventoryOperationStatus.InvalidClass
        && result.Snapshot.Equipment.Count == 0;
}

static bool InvalidSlotFails()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, equipmentSlot: InventoryEquipmentSlot.Unspecified);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));

    return result.Status == InventoryOperationStatus.InvalidSlot
        && result.Snapshot.Equipment.Count == 0;
}

static bool InvalidLevelFails()
{
    using var fixture = InventoryFixture.Create(level: 0);
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, requiredLevel: 1);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));

    return result.Status == InventoryOperationStatus.InvalidLevel
        && result.Snapshot.Equipment.Count == 0;
}

static bool ValidUnequipReturnsToInventory()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, targetSlotIndex: 4);
    var equipVersion = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var equip = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, equipVersion));
    var unequip = fixture.Inventory.UnequipItem(new InventoryUnequipItemCommand(fixture.Actor, InventoryEquipmentSlot.OffHand, equip.Snapshot.InventoryVersion));

    return equip.Accepted
        && unequip.Accepted
        && unequip.Snapshot.Equipment.Count == 0
        && unequip.Snapshot.Slots.Single(slot => slot.SlotIndex == 0).ItemInstanceId == item.ItemInstanceId;
}

static bool UnequipFullInventoryFails()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, targetSlotIndex: 0);
    var equipVersion = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var equip = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, equipVersion));
    fixture.Inventory.FillInventoryForTesting(fixture.Actor);
    var fullVersion = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;

    var unequip = fixture.Inventory.UnequipItem(new InventoryUnequipItemCommand(fixture.Actor, InventoryEquipmentSlot.OffHand, fullVersion));
    var after = fixture.Inventory.GetOrCreateInventory(fixture.Actor);

    return equip.Accepted
        && unequip.Status == InventoryOperationStatus.InventoryFull
        && after.Equipment.Single().ItemInstanceId == item.ItemInstanceId
        && after.Slots.All(slot => slot.ItemInstanceId is not null);
}

static bool ZeroDurabilityHasNoAttributes()
{
    using var fixture = InventoryFixture.Create();
    var item = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, EquipmentRarity.Rare, durability: 0);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;

    var result = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, item.ItemInstanceId, InventoryEquipmentSlot.OffHand, version));
    var equipment = result.Snapshot.Equipment.Single();

    return result.Accepted
        && equipment.Durability == 0
        && !equipment.AttributesActive
        && equipment.DefenseBonus == 0
        && result.Snapshot.DefenseBonus == 0;
}

static bool PendingRewardClaimed()
{
    using var fixture = InventoryFixture.Create();
    var pending = fixture.Inventory.CreatePendingWoodenShieldForTesting(fixture.Actor, EquipmentRarity.Normal, currencyBalance: 3);
    var version = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;

    var result = fixture.Inventory.ClaimPendingReward(new InventoryClaimPendingRewardCommand(fixture.Actor, pending.PendingRewardId, version));

    return result.Accepted
        && result.Snapshot.PendingRewards.Count == 0
        && result.Snapshot.Slots.Single(slot => slot.SlotIndex == 0).ItemInstanceId == pending.ItemInstanceId
        && result.Snapshot.CurrencyBalance == 3;
}

static bool DefenseComparisonReportsDelta()
{
    using var fixture = InventoryFixture.Create();
    var current = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, EquipmentRarity.Rare, targetSlotIndex: 0);
    var candidate = fixture.Inventory.CreateWoodenShieldForTesting(fixture.Actor, EquipmentRarity.Normal, targetSlotIndex: 1);
    var equipVersion = fixture.Inventory.GetOrCreateInventory(fixture.Actor).InventoryVersion;
    var equip = fixture.Inventory.EquipItem(new InventoryEquipItemCommand(fixture.Actor, current.ItemInstanceId, InventoryEquipmentSlot.OffHand, equipVersion));

    var comparison = fixture.Inventory.CompareOffHand(fixture.Actor, candidate.ItemInstanceId);

    return equip.Accepted
        && comparison.CurrentDefense == 5
        && comparison.CandidateDefense == 3
        && comparison.DefenseDelta == -2
        && comparison.CurrentBlockChancePercent == 2
        && comparison.CandidateBlockChancePercent == 0
        && comparison.BlockChanceDeltaPercent == -2;
}

static InventoryEquipmentCheck Check(string name, bool passed) => new(name, passed);

internal readonly record struct InventoryEquipmentCheck(string Name, bool Passed);

internal sealed class InventoryFixture : IDisposable
{
    private InventoryFixture(string storePath, InventoryActor actor, InventoryEquipmentService inventory)
    {
        StorePath = storePath;
        Actor = actor;
        Inventory = inventory;
    }

    public string StorePath { get; }
    public InventoryActor Actor { get; }
    public InventoryEquipmentService Inventory { get; }

    public static InventoryFixture Create(int level = 1)
    {
        var storePath = TestPaths.CreateTempDirectory("vs017-inventory-equipment");
        var actor = new InventoryActor("character-vs017", "Knight", level);
        var inventory = new InventoryEquipmentService(storePath, new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        return new InventoryFixture(storePath, actor, inventory);
    }

    public void Dispose() => TestPaths.DeleteDirectory(StorePath);
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        _utcNow = _utcNow.AddSeconds(1);
        return _utcNow;
    }
}

internal static class TestPaths
{
    public static string CreateTempDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "divinity", name, Guid.NewGuid().ToString("N"));
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
