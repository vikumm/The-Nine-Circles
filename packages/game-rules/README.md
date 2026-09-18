# Game Rules

This package contains server-side rules and versioned catalog values for MMO-VS1.

Current scope:

- provide the VS-008 Knight level 1 catalog;
- validate and normalize character names;
- create/select exactly one Knight slot per account;
- keep initial stats and safe spawn server-side;
- provide VS-010 shared movement normalization and cardinal-facing helpers;
- provide VS-011 visual-only client prediction and reconciliation helpers;
- provide VS-012 Moss Slime level-1 balance values and AI state names;
- provide VS-013 Basic Slash damage and cooldown constants;
- provide VS-014 Shield Bash damage, MP, cooldown, stun and skill progression constants;
- provide VS-015 death-screen duration and tier-0 wooden shield durability/attribute catalog values;
- provide VS-016 Moss Slime reward catalog values for XP, training currency and deterministic item-drop thresholds;
- provide VS-017 inventory/equipment rules for 20 slots, optimistic versioning, OffHand equip/unequip, bind-on-equip, pending reward claim and defense comparison;
- provide VS-018 checkpoint metadata for reconnect-safe HP/MP restoration and unsafe checkpoint fallback.

Out of scope here:

- four vocations;
- client-authored rewards;
- public loot;
- trade;
- marketplace;
- crafting;
- multiple monster templates.

Unity consumes `Movement/` as a local package through `apps/game-client-unity/Packages/manifest.json`. Prediction helpers are reversible visual state only; server snapshots and corrections remain authoritative.

Monster balance values are catalog data. Target selection, path cadence, damage, death and respawn authority remain in World Runtime.

Basic Slash damage values are catalog data. Final attack validation, RNG, HP mutation and death decisions remain in World Runtime.

Shield Bash values are catalog data. World Runtime remains responsible for validating class, MP, cooldown, range, target state, stun application and skill XP.

Death and equipment values are catalog data. World Runtime remains responsible for HP mutation, death/respawn timing, durability loss and whether equipment attributes are active.

Reward values are catalog data. World Runtime remains responsible for choosing eligible participants, rolling loot, committing grants and enforcing idempotency.

Inventory and equipment state are server-side rules. Clients may request equip/unequip and render snapshots, but item owner, item location, bind state, durability, defense, block chance, pending rewards and currency remain server-owned.

Reconnect checkpoint metadata is server-side state. Clients may request reconnect with a short-lived token, but they must not author HP/MP, position, XP, inventory or equipment.
