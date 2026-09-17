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
- VS-012 monster AI tests for Moss Slime spawn, aggro, leash, path cadence, target loss, attack event and respawn.
- VS-013 basic attack tests for damage, mitigation, variance, criticals, range, cooldown, invalid targets and sequence replay.
