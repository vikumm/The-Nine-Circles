# ADR-0026: VS-017 Inventory And Equipment Authority

## Context

VS-017 requires a 20-slot inventory, optimistic inventory versioning, OffHand equipment for the Wooden Shield, defense comparison, bind-on-equip and pending reward claim behavior.

The GDD keeps client authority prohibited. The client may request equip/unequip and render server deltas, but it must not author owner, location, bind, rarity, durability, defense, block chance, pending reward or currency.

## Decision

Inventory and equipment rules live in `packages/game-rules` behind `InventoryEquipmentService`. The service persists a local VS-017 document containing `item_instances`, `inventory_slots`, `equipment_loadouts`, `pending_rewards` and wallet read state, guarded by an exclusive file lock.

Mutations require the current `inventory_version`. Server validation checks owner, item location, class, level, slot, bind and durability state before moving an item between inventory and OffHand equipment.

The gateway routes `EquipItemIntent` and `UnequipItemIntent` after session join and character ownership verification. It maps accepted server snapshots to `InventoryDelta`; rejected mutations return `ERROR_CODE_INVENTORY_REJECTED`.

Pending reward claim is implemented in the server rule service for VS-017 tests. A public client claim contract is intentionally not invented because the GDD only lists equip/unequip intents for this task.

## Consequences

- Optimistic version conflicts do not mutate inventory.
- Equip removes the item from inventory, writes OffHand loadout and binds the item.
- Unequip requires a free slot and preserves the equipped item when inventory is full.
- Zero-durability equipment grants no attributes through `attributes_active = false`.
- Unity can render inventory, tooltip, comparison and currency state but cannot decide any authoritative item outcome.
- Integration between the VS-016 reward transaction store and this VS-017 inventory read model remains a future consolidation risk because VS-017 does not allow editing `services/world-runtime`.
