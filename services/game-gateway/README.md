# Game Gateway

This folder contains the Game Gateway service for MMO-VS1.

Current scope:

- compile as an ASP.NET Core service;
- expose `/healthz` for bootstrap smoke checks;
- validate Protobuf v1 `ClientEnvelope` messages;
- consume VS-006 game tickets from `ClientHello.game_ticket`;
- accept VS-007 WebSocket handshakes at `/protocol/v1/ws`;
- create an authenticated in-memory world session;
- verify VS-008 persisted Knight ownership on `JoinWorld`;
- acquire and renew a single-owner session lease;
- return `JoinAccepted` and a minimal `WorldSnapshot` from server-side character data.
- validate Training Field map/content hash through World Runtime before accepting `JoinWorld`;
- route `MoveIntent` to World Runtime after join;
- rate limit `MoveIntent` to 20 messages per second per authenticated session;
- return authoritative `WorldSnapshot`, `Correction` or `ServerError` responses for movement.

Out of scope here:

- durable multi-node WSS session persistence;
- reconnect leases;
- client-side prediction/reconciliation;
- combat;
- inventory.

VS-007 note:

- local development uses `ws://`; production must expose the route as WSS behind TLS;
- `ReconnectRequest` is a controlled protocol stub only;
- character ownership comes from the VS-008 character store.

VS-010 note:

- the Gateway does not decide final movement positions locally;
- movement decisions are delegated to `services/world-runtime`;
- normal disconnect asks World Runtime to persist the last authoritative checkpoint.
