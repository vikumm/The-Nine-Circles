# ADR-0015: VS-006 Game Ticket

## Context

VS-006 requires a game ticket issued by Platform API and consumed once by Game Gateway. The ticket must be short-lived, bound to account id, build id, protocol version and nonce, and rejected safely when malformed, expired, reused or mismatched.

VS-005 launcher authentication is complete enough to provide an account identity. VS-007 WSS authentication and session leases are not in scope yet.

## Decision

Create a shared `GameTickets` module in `packages/contracts-proto` for the minimal issue and consume model.

Platform API exposes `POST /launcher/game-ticket` and issues a 30-second opaque bearer ticket. In development and tests only, `X-Divinity-Dev-Account-Id` may stand in for an authenticated account when `DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER=true`.

Game Gateway consumes `ClientHello.game_ticket` through the shared service. A valid consume returns `CLIENT_HELLO_ACCEPTED_NO_SESSION` because a durable authenticated WSS session is reserved for VS-007.

The Protobuf `ErrorCode` enum is extended with game-ticket rejection codes. No existing fields are renumbered or redefined.

Use a file-backed shared store keyed by ticket hash for VS-006. Atomic consume is represented by moving the active ticket hash record into a consumed directory with exclusive create semantics.

## Consequences

The ticket secret is returned to the launcher/client only once and is never written to the store or audit log in clear text.

The file-backed store is suitable for local smoke gates and keeps VS-006 free of additional package dependencies. It is not a final distributed production design.

Ticket cleanup, Valkey-backed atomic consume and WSS session materialization remain future work unless a later GDD task explicitly brings them into scope.
