# Divinity.ContentSchema

Versioned content schema and validation library for MMO-VS1.

This package validates the source content used by the VS-004 builder:

- `training-field-01` map bounds, blocked cells, regions, spawns, safe spawns and triggers;
- Knight skills `knight_basic_slash` and `knight_shield_bash_r1`;
- tier-0 item `knight_wooden_shield_t0`;
- loot table `mob_moss_slime_l1`;
- deterministic content hash shared by generated client and server artifacts.

Authority remains server-side. Client artifacts are visual/read-only projections and must not become a source of authoritative walls, spawns, safe zones, loot, rarity or final attributes.
