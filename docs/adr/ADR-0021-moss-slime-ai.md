# ADR-0021: VS-012 Moss Slime AI

## Context

VS-012 introduces `mob_moss_slime_l1` in `map_training_field_01`. The GDD requires server-side Idle, Wander, Chase, Attack, Return and Dead states; aggro at 6 units; leash at 10 units from spawn; path recalculation capped at 4/s; respawn 8s after valid death; and no reward when a slime returns.

The VS-012 allowed-file list does not include `packages/contracts-proto`, while the task text mentions `EntitySpawned` and `EntityDespawned`. The current protocol has `WorldSnapshot`, `EntityState`, preliminary `CombatEvent` and `RewardGranted`, but no spawn/despawn messages.

## Decision

Implement Moss Slime authority in `services/world-runtime/Monsters` and keep balance constants in `packages/game-rules/Monsters`.

The runtime loads monster spawns from the authoritative Training Field server artifact, caps active Moss Slimes at 30 and exposes monster presence through `WorldSnapshot` monster `EntityState` rows. Dead monsters are omitted from snapshots until the 8s respawn timer returns them to Idle at their spawn.

Pathing remains intentionally simple for the vertical slice: a direct authoritative step toward the target or spawn, guarded by map collision checks, with recalculation no more often than every 250ms. This satisfies the 4/s cap without introducing advanced pathfinding.

The Attack state emits a preliminary `CombatEvent` using the existing contract. Full combat resolution, rewards, loot and multiplayer participation stay out of VS-012.

## Consequences

The client can render Slime state from snapshots but cannot choose target, path, damage, death, reward or respawn. World Runtime is the only authority for AI decisions.

The absence of `EntitySpawned`/`EntityDespawned` is explicit technical debt for a later contracts task. For VS-012, spawn/despawn is represented by monster `EntityState` appearing in or disappearing from `WorldSnapshot`.

Deterministic fixture tests cover the GDD acceptance gates without requiring Unity or a live gateway.

## Unresolved

Final network fan-out of monster snapshots, spawn/despawn event contracts, full combat math, XP/loot reward distribution, multi-player contribution rules and production pathfinding are not defined in VS-012.
