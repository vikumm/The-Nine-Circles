# VS-020 Security Notes

## Scope

VS-020 hardens the existing vertical slice. It does not introduce new authentication flows, public production exposure, Kubernetes, new gameplay systems or client authority.

## Server Authority

The server remains authoritative for:

- ticket issuance and consumption;
- session lease and reconnect ownership;
- map join validation;
- movement, collision and correction;
- combat, damage, stun, death and cooldowns;
- reward idempotency, currency and drops;
- inventory versioning and equipment persistence.

The Unity client may render state and send intents only. It must not enforce rate limits as a source of truth, decide reward outcomes, mint reconnect tokens, extend ticket TTLs or repair authoritative state.

## Hardening Controls

- Payloads above 64 KiB are rejected before full Protobuf parsing.
- Malformed or truncated Protobuf payloads return controlled `ServerError` results.
- Anonymous handshake spam is limited before WebSocket acceptance.
- Authenticated messages are limited by category: move, combat, inventory, heartbeat, join and reconnect.
- Offensive/defensive cases are executed only by local tests and `tools/load-bots` profiles.

## Log And Trace Hygiene

Structured logs must keep only pseudonymous identifiers and operational fields:

- timestamp;
- service;
- environment;
- trace id;
- connection id;
- account pseudonym;
- character id;
- map id;
- event type;
- result;
- error code;
- latency.

Logs and traces must not contain passwords, access tokens, game tickets, reconnect tokens, full payload dumps or private OIDC material.

## Rollback

If hardening blocks legitimate load, revert only the specific limit or observability change that caused the regression, keep approval gates failing, and record the incident in the runbook or a follow-up ADR before reopening the gate.
