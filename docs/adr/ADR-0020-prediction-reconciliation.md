# ADR-0020: VS-011 Prediction and Reconciliation

## Context

VS-011 requires responsive local movement in Unity while preserving server authority from VS-010. The client may predict its own visual position, but authoritative snapshots and corrections from the server must always win.

The GDD also requires North, South, East and West orientation to follow a shared four-direction function, with diagonal ties preserving the last valid orientation.

## Decision

Keep prediction as a visual-only client model backed by shared movement code in `packages/game-rules/Movement`. Unity consumes that folder as the local package `com.divinity.movement-rules`, avoiding a second orientation implementation in Unity scripts.

The predictor applies local WASD/click-target intent immediately, tracks pending input sequences, discards inputs acknowledged by `ServerEnvelope.ack_sequence`, reapplies remaining inputs after an authoritative snapshot and ignores stale snapshots.

Small visual error is corrected with a smoothing factor. Error above the snap threshold is moved immediately to the reconciled predicted position. `Correction` uses the same reconciliation path as `WorldSnapshot` without giving the client any authority over collision, bounds or final position.

## Consequences

Unity gets immediate local feedback without sending final position, trusted speed or collision results.

The server runtime remains authoritative and does not consume client prediction state. Prediction can be disabled by ignoring local prediction and rendering server snapshots directly while preserving VS-010 `MoveIntent`.

The VS-011 automated tests exercise the shared predictor outside the Unity editor. A Unity editor smoke method exists for local batch-mode validation when Unity is available.

## Unresolved

Final animation blending, remote-entity interpolation, camera polish and production profiling are not defined for VS-011.
