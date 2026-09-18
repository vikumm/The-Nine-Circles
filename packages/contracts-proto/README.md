# Contracts Proto

This package contains the VS-003 protocol contracts and the VS-006 game-ticket primitives.

Scope:

- versioned Protobuf package `divinity.protocol.v1`;
- C# generation through the .NET build;
- short-lived game-ticket issue and consume models;
- client messages are intents only;
- VS-013 `AttackIntent`, `CombatEvent`, `SkillStateChanged` and attack rejection code;
- VS-014 `CastIntent`, `CharacterProgressed`, stun status effects and cast rejection code;
- VS-015 server-authored `CombatEvent.kill_id` and preliminary equipment durability fields in `InventoryDelta`;
- VS-016 server-authored `RewardGranted`, `InventoryDelta` and `CharacterProgressed` reward outputs;
- VS-017 `EquipItemIntent`, `UnequipItemIntent`, `InventoryDelta` and `ERROR_CODE_INVENTORY_REJECTED`;
- VS-018 `JoinAccepted.reconnect_token`, `reconnect_ttl_seconds` and `ERROR_CODE_RECONNECT_REJECTED`;
- no balance, AI, pathfinding, reward persistence or UI behavior.

The conceptual envelope size limit is 64 KiB. Gateways must reject larger payloads before expensive parsing.

Game-ticket secrets are opaque bearer values. Store and audit code must handle only ticket hashes or non-secret metadata after issue.

VS-014 Shield Bash keeps client messages intent-only. `CastIntent` contains only `skill_id`, `target_entity_id` and `action_id`; MP, cooldown completion, stun duration, skill XP and rank are server-authored outputs.

VS-015 keeps death and durability server-authored. Clients receive `kill_id`, death-screen status effects and equipment durability values as display state only.

VS-016 keeps rewards server-authored. Clients may render reward, inventory and progression payloads, but do not submit reward keys, loot decisions, currency, XP or item instances.

VS-017 keeps inventory and equipment server-authored. Clients may send item/slot/version intents only; owner, local item location, bind, rarity, durability mutation, defense, block chance, pending reward and currency remain server outputs.

VS-018 keeps reconnect server-authored. Clients present a short-lived token after a fresh `ClientHello`; they do not provide HP/MP, position, inventory, equipment, XP or reward replay decisions.
