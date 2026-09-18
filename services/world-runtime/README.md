# World Runtime Bootstrap

This folder contains the World Runtime bootstrap plus the VS-010 authoritative Training Field movement runtime and the VS-012 Moss Slime AI runtime.

Scope for VS-001:

- compile as a bootstrap executable;
- expose project metadata;
- load the generated server authoritative map artifact;
- validate Training Field content hash and safe spawn for join admission;
- apply authoritative `MoveIntent` for WASD and click-to-move;
- validate speed, sequence, bounds, blocked cells and dead/stunned state;
- save position checkpoints periodically, on disconnect and on graceful shutdown;
- run the Moss Slime level-1 server-side AI over authoritative map spawns;
- cap active Moss Slimes at 30, detect aggro within 6 units, enforce 10-unit leash, cap path recalculation to 4/s, return/regenerate without reward and respawn after 8s valid death;
- emit monster `EntityState` through `WorldSnapshot` and preliminary `CombatEvent` hits using existing contracts;
- validate VS-013 `knight_basic_slash` target, range, cooldown, state and sequence;
- calculate server-side Basic Slash damage, variance, crit and target HP;
- emit server-authored `CombatEvent` and controlled attack rejection;
- validate VS-014 `knight_shield_bash_r1` class, MP, cooldown, range, target, stun state and `skill_action_id`;
- calculate server-side Shield Bash damage, apply 1.25 second stun and grant +1 skill XP only on valid hit;
- cap VS-014 Shield Bash rank at 2 with rank 2 requiring 20 XP;
- decide VS-015 character death when HP reaches zero, expose a 5 second death-screen effect and respawn at Safe Spawn;
- apply one durability loss to durable equipped items on death, keeping zero-durability items equipped but inactive for attributes;
- assign unique `kill_id` values to valid monster deaths and reject delayed attacks against dead monsters without duplicate death output;
- commit VS-016 reward grants transactionally with idempotent `reward_key`, append-only currency ledger, item instance, inventory slot or pending reward, XP/progress, outbox and audit records;
- retain VS-018 abrupt combat disconnect actors for up to 10 seconds, persist reconnect checkpoints with HP/MP metadata and fall back to Safe Spawn for unsafe checkpoints;
- do not implement trade, marketplace, public ground loot, party loot, crafting, long offline reconnect or optimized pathfinding.

Future work:

- Later tasks are expected to harden reconnect at load and soak-test scale.
