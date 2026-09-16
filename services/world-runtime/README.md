# World Runtime Bootstrap

This folder contains the World Runtime bootstrap plus the VS-010 authoritative Training Field movement runtime.

Scope for VS-001:

- compile as a bootstrap executable;
- expose project metadata;
- load the generated server authoritative map artifact;
- validate Training Field content hash and safe spawn for join admission;
- apply authoritative `MoveIntent` for WASD and click-to-move;
- validate speed, sequence, bounds, blocked cells and dead/stunned state;
- save position checkpoints periodically, on disconnect and on graceful shutdown;
- do not implement AI, combat, rewards, reconnect or optimized pathfinding.

Future work:

- VS-011 introduces client prediction and reconciliation on top of authoritative movement.
