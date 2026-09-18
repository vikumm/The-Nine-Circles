# ADR-0024: VS-015 Death, Respawn And Durability Authority

## Context

VS-015 requires the server to decide character death, the 5 second death screen, respawn at Safe Spawn, post-respawn HP/MP, monster `kill_id` and durability loss for durable equipped items.

VS-017 is still expected to implement the complete inventory/equipment workflow. VS-018 is still expected to implement complete reconnect behavior.

## Decision

World Runtime owns the authoritative death transition. When character HP reaches zero, the runtime marks the actor as `Dead`, emits a server-authored death-screen status effect for 5 seconds, applies one durability loss to durable equipped items and schedules respawn at Safe Spawn.

Durability support is intentionally preliminary. A server-side equipment runtime tracks equipped durable items in memory for this gate, exposes durability through `InventoryDelta`, and removes attribute contribution when durability reaches zero while keeping the item equipped.

Monster deaths retain World Runtime ownership of `kill_id`. `CombatEvent.kill_id` is populated only on the first valid lethal hit, and delayed attacks against dead monsters do not create duplicate death output or Shield Bash skill XP.

## Consequences

- Clients may render death, timer, respawn, durability and `kill_id`, but do not decide any of them.
- No XP or level is lost on character death.
- The implementation covers VS-015 deterministic tests without implementing repair, loot, pending reward or full reconnect persistence.
- Full item ownership, equip validation, durable persistence and reconnect replay/idempotency remain open for later GDD tasks.
