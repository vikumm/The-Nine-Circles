# Test Fixtures Bootstrap

This package is reserved for deterministic fixtures used by automated tests.

Scope for VS-001:

- expose bootstrap metadata;
- support smoke tests without external test dependencies;
- do not add gameplay content, protocol payloads or balance fixtures.

Current fixture gates:

- VS-001 smoke tests;
- VS-006 game-ticket tests;
- VS-007 session tests;
- VS-008 character tests;
- VS-009 map import tests;
- VS-010 movement tests;
- VS-011 prediction tests;
- VS-012 monster AI tests for Moss Slime spawn, aggro, leash, path cadence, target loss, attack event and respawn;
- VS-013 basic attack tests for damage, mitigation, variance, criticals, range, cooldown, invalid targets and sequence replay;
- VS-014 Shield Bash tests for MP cost, cooldown, stun, idempotency, class gate and skill XP;
- VS-015 death/respawn tests for Dead state, 5 second timer, Safe Spawn, checkpoint, durability loss, zero-durability attributes, unique `kill_id`, delayed dead-target attack and disconnect during death;
- VS-016 reward transaction tests for valid grant, idempotency, retry, deadlock, pending reward, append-only ledger, outbox and deterministic QA loot;
- VS-017 inventory/equipment tests for item move, optimistic version, OffHand equip/unequip, owner/class/slot/level validation, zero durability, pending reward claim and defense comparison;
- VS-018 reconnect tests for token rotation, 30 second session grace, 10 second combat retention, actor uniqueness, reward idempotency, safe checkpoint fallback, inventory/equipment persistence and gateway restart.
- VS-019 load bot tests for one-bot E2E, configurable ramp, replay rejection, slow client, 20% batch reconnect, gateway restart, graceful world shutdown, 50 concurrent combat bots and duplicate reward count equal to 0.
