# Session Lease Security

Status: VS-008.

## Scope

VS-007 authenticates a WebSocket connection with a previously issued game ticket. VS-008 verifies persisted character ownership before acquiring a single-owner session lease.

It does not implement full reconnect, movement, combat, inventory or multi-node lease persistence.

## Authority

The Gateway validates the first WSS message before creating a session:

- payload size limit;
- Protobuf parse;
- protocol version;
- game-ticket presence and validity;
- one-time ticket consume;
- anonymous handshake rate limit.

The client does not choose a trusted account id. The account id comes from the consumed ticket.

## Lease Policy

Only one connection owns the active lease for a character.

VS-007/VS-008 use an in-memory lease store for the local gate. A normal disconnect releases the lease and records a session event. Heartbeat renews the lease for 30 seconds.

Since VS-008, `JoinWorld.character_id` must match a persisted Knight owned by the authenticated account. Client-provided account ids, stats, map ids and positions are never trusted.

## Logging

Gateway session logs must include connection id, account pseudonym, result and error code.

Logs must not include game-ticket secrets, reconnect tokens or raw account ids.
