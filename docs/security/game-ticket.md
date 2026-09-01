# Game Ticket Security

Status: VS-006.

## Scope

The VS-006 game ticket is a short-lived bearer secret used to bridge launcher authentication and the future authenticated WSS handshake.

It does not create a long session, reconnect token, character authorization, movement, combat, inventory or persistence behavior.

## Authority

Only Platform API issues game tickets.

Only Game Gateway consumes game tickets.

The client receives the ticket as an opaque bearer secret and sends it in `ClientHello.game_ticket`. The client must not validate, extend, authorize or derive gameplay authority from the ticket.

## Binding

Each issued ticket is bound server-side to:

- account id;
- build id;
- protocol version;
- client nonce;
- issued time;
- expiration time.

The TTL is 30 seconds.

## Secret Handling

The ticket value must not appear in URLs, traces, logs or command-line arguments.

The shared store uses the SHA-256 hash of the ticket as the lookup key and stores only ticket metadata. Audit records include account id, build id, protocol version, expiration and a nonce hash, never the ticket secret.

## Current Storage

VS-006 uses a minimal file-backed store selected by `DIVINITY_GAME_TICKET_STORE_PATH`. Atomic consume is implemented by moving the active hash record to the consumed directory with an exclusive move.

This is intentionally dependency-light for the vertical-slice gate. Distributed production use of Valkey or another managed atomic store remains a later hardening decision.
