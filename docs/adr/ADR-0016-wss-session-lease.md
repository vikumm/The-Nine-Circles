# ADR-0016: VS-007 WSS Handshake and Session Lease

## Context

VS-007 must prove that a client can authenticate to the Game Gateway over WebSocket, consume a game ticket exactly once, create a world session and prevent two connections from controlling the same character.

VS-008 character persistence and full reconnect are not implemented yet.

## Decision

Add `GET /protocol/v1/ws` to Game Gateway. The endpoint accepts WebSocket upgrades, applies an anonymous 3/min rate limit by remote address, rejects oversized messages before parsing and requires `ClientHello` as the first message.

Consume the VS-006 game ticket during the first message and create an authenticated in-memory world session. After that, require `JoinWorld` to acquire a session lease before `Heartbeat` can renew it.

Use an in-memory `GatewaySessionManager` for `world_sessions` and `session_leases` semantics in the local vertical-slice gate. Normal disconnect releases the lease and records a session event.

Until VS-008 introduces persistent characters, use a deterministic server-side stub character id derived from the authenticated account. This keeps ownership verification server-side without inventing character storage.

Extend the Protobuf `ErrorCode` enum with session handshake, lease, heartbeat and reconnect-stub codes. Existing message fields and enum numbers are left intact.

## Consequences

The Gateway can validate a real WebSocket handshake and join flow without adding external dependencies or gameplay systems.

Only one active connection can own the character lease in a process. Multi-node/distributed leases remain future work.

Reconnect remains explicitly unsupported and returns a controlled protocol response.
