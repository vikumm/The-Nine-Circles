# VS-017 Inventory And Equipment Test Notes

Status: implemented for the MMO-VS1 vertical slice gate.

Mandatory checks covered by `packages/test-fixtures/inventory-equipment-tests`:

- initial inventory has 20 slots;
- valid item move uses optimistic version;
- stale inventory version rejects mutation;
- valid OffHand equip removes the item from inventory and binds it;
- missing item fails;
- item owned by another character fails;
- invalid class fails;
- invalid slot fails;
- invalid level fails;
- valid unequip returns the item to the first free slot;
- unequip with full inventory fails without item loss;
- zero-durability equipment grants no attributes;
- pending reward can be claimed into inventory;
- defense comparison reports gain/loss.

Commands:

```bash
dotnet build packages/test-fixtures/inventory-equipment-tests/Divinity.InventoryEquipmentTests.csproj --configuration Release
dotnet run --project packages/test-fixtures/inventory-equipment-tests/Divinity.InventoryEquipmentTests.csproj --configuration Release --no-build
```

Out of scope:

- trade;
- marketplace;
- crafting;
- complete multi-slot equipment;
- NPC repair.
