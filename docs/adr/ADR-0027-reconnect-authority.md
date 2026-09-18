# ADR-0027: VS-018 Reconnect Authority

## Context

VS-018 requires reconnect inside a short session window, reassociation to the same actor, preservation of checkpoint, HP/MP, inventory/equipment and protection against duplicate `RewardGranted` replay.

The client cannot be trusted to restore state or decide whether a lease is still valid.

## Decision

Gateway owns reconnect token and lease authority. It persists `world_sessions`, `session_leases` and hashed reconnect tokens in a VS-018 local store. `JoinAccepted` carries a short-lived reconnect token, and successful reconnect consumes that token, rotates a new one and transfers the lease to the new connection.

World Runtime owns actor and checkpoint authority. Abrupt disconnect during recent combat retains the actor for up to 10 seconds; grace expiry saves a checkpoint and removes the actor. Checkpoints now carry HP/MP metadata and unsafe checkpoint positions fall back to Safe Spawn.

Reconnect returns current `JoinAccepted`, `WorldSnapshot` and `InventoryDelta`. It does not reemit historical `RewardGranted`; reward idempotency remains enforced by VS-016 `reward_key`.

## Consequences

- A fresh game ticket is still required before reconnect.
- Replayed reconnect tokens fail after first successful use.
- Competing reconnect attempts can result in only one active lease owner.
- Restarting the gateway preserves reconnect leases/tokens through the local store.
- Long offline reconnect, shard transfer, login queue and rollback of progress remain out of scope.
