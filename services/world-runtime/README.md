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
- do not implement Shield Bash, loot, XP reward distribution, reconnect or optimized pathfinding.

Future work:

- VS-014 is expected to add Shield Bash and skill XP without moving authority to the client.
