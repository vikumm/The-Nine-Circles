# ADR-0029: VS-020 hardening and observability gate

## Context

VS-020 closes the MMO-VS1 initial backlog with hardening, category rate limits, offensive/defensive tests, logs, metrics, traces, client profiling, soak validation and an operating runbook.

The GDD requires server authority to remain intact. Hardening must not relax movement, combat, inventory, reward, session, ticket or reconnect validation to pass tests.

## Decision

Keep the existing v1 protocol unchanged and add hardening around the current message categories:

- reject payloads larger than 64 KiB before full Protobuf parsing;
- rate limit authenticated message categories separately for move, combat, inventory, heartbeat, join and reconnect;
- keep anonymous handshake throttling before WebSocket acceptance;
- add OpenTelemetry `ActivitySource` and `Meter` entry points to platform API, game gateway and world runtime;
- keep structured log fields pseudonymous and prohibit password, access token, game ticket, reconnect token and full sensitive payload logging;
- expand `tools/load-bots` with a VS-020 hardening profile and a full soak profile.

The Unity client receives only an editor QA profiling bootstrap. It does not receive authoritative gameplay logic.

## Consequences

The final local gate can exercise protocol hardening, offensive/defensive cases and 100 automated bot flows without changing contracts.

The full 2 hour soak and Windows Unity FPS gate remain release validation steps because they require a long-running environment and Unity batch execution.

Future production observability can wire the existing metric and trace sources to hosted collectors without changing protocol or game rules.
