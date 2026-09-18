# Game Gateway

This folder contains the Game Gateway service for MMO-VS1.

Current scope:

- compile as an ASP.NET Core service;
- expose `/healthz` for bootstrap smoke checks;
- validate Protobuf v1 `ClientEnvelope` messages;
- consume VS-006 game tickets from `ClientHello.game_ticket`;
- accept VS-007 WebSocket handshakes at `/protocol/v1/ws`;
- create an authenticated in-memory world session;
- verify VS-008 persisted Knight ownership on `JoinWorld`;
- acquire and renew a single-owner session lease;
- return `JoinAccepted` and a minimal `WorldSnapshot` from server-side character data.
- validate Training Field map/content hash through World Runtime before accepting `JoinWorld`;
- route `MoveIntent` to World Runtime after join;
- rate limit `MoveIntent` to 20 messages per second per authenticated session;
- return authoritative `WorldSnapshot`, `Correction` or `ServerError` responses for movement.
- route VS-013 `AttackIntent` to World Runtime after join;
- return server-authored `CombatEvent`, `SkillStateChanged` or `ServerError` responses for Basic Slash.
- route VS-014 `CastIntent` for `knight_shield_bash_r1` to World Runtime after join;
- return server-authored `CombatEvent`, `SkillStateChanged`, `CharacterProgressed` or `ServerError` responses for Shield Bash;
- route VS-017 `EquipItemIntent` and `UnequipItemIntent` after join and character ownership verification;
- return server-authored `InventoryDelta` or `ERROR_CODE_INVENTORY_REJECTED` responses for inventory/equipment mutations;
- validate VS-018 `ReconnectRequest` with a fresh `ClientHello`, hashed token, account match and active lease;
- rotate reconnect tokens and transfer the actor lease to the new connection.

Out of scope here:

- durable multi-node WSS session persistence;
- client-side prediction/reconciliation;
- public loot, trade, marketplace and crafting;
- client-authoritative inventory.

VS-007 note:

- local development uses `ws://`; production must expose the route as WSS behind TLS;
- `ReconnectRequest` is a controlled protocol stub only;
- character ownership comes from the VS-008 character store.

VS-010 note:

- the Gateway does not decide final movement positions locally;
- movement decisions are delegated to `services/world-runtime`;
- normal disconnect asks World Runtime to persist the last authoritative checkpoint.

VS-013 note:

- the Gateway does not calculate damage, range, cooldown, crit, HP or death;
- Basic Slash decisions are delegated to `services/world-runtime`;
- the client still sends only intents.

VS-014 note:

- the Gateway does not calculate MP cost, stun duration, skill XP, rank, cooldown or target state;
- Shield Bash decisions are delegated to `services/world-runtime`;
- the client still sends only `CastIntent` with skill id, target id and action id.

VS-017 note:

- the Gateway does not calculate item owner, bind, rarity, durability, defense, block chance or currency;
- inventory/equipment decisions are delegated to `packages/game-rules`;
- the client sends only item/slot/version intents and renders `InventoryDelta`.

VS-018 note:

- the Gateway stores reconnect tokens hashed and short-lived;
- reconnect requires a fresh game ticket plus the previous reconnect token;
- old rewards are not replayed; the Gateway returns current join, snapshot and inventory state.
