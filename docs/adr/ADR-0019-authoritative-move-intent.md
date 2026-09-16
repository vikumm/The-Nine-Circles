# ADR-0019: VS-010 Authoritative MoveIntent

## Context

VS-010 requires continuous movement for the vertical slice while preserving server authority. Clients may express movement intent, but the server must own speed, diagonal normalization, collision, bounds, character state, sequence acceptance, snapshots and checkpoints.

The Training Field map imported in VS-009 is the authoritative source for bounds, blocked cells and safe spawn validation.

## Decision

Add movement execution to `services/world-runtime` and route authenticated `MoveIntent` messages from `services/game-gateway` after `JoinWorld` is accepted.

Keep shared, deterministic movement math in `packages/game-rules` for direction normalization and cardinal facing. World Runtime applies the final authoritative displacement using a 4.5 units/s speed cap at the VS-010 20 Hz movement intent cadence.

Gateway enforces a per-session `MoveIntent` rate limit of 20 messages per second, then delegates movement validation to World Runtime. World Runtime rejects repeated or older sequence numbers, blocked/out-of-bounds click targets, wall crossings and dead/stunned character states. It emits `WorldSnapshot` at 10 Hz and persists checkpoints periodically, on disconnect and on graceful shutdown.

## Consequences

Unity remains non-authoritative and can send only direction or click-target intent. It does not send final position, collision authority, speed authority or checkpoint state.

The current click-to-move behavior validates a direct segment against authoritative collision. Full pathfinding, prediction, reconciliation and reconnect state restoration remain later tasks.

The character store gains a checkpoint collection so VS-010 can persist position without introducing a production database.

## Unresolved

The GDD does not define the final pathfinding algorithm, production persistence backend, multi-node runtime sharding, or client prediction/reconciliation protocol for VS-010.
