# ADR-0003: Server Authority Boundary

Status: Accepted

## Context

The GDD requires server-side authority for gameplay, security and persistence. Clients submit intents.

## Decision

All authoritative decisions for movement, collision, combat, rewards, inventory, economy, checkpoints and persistence belong to server-side processes or approved shared server rule packages.

## Consequences

- Launcher and Unity client remain non-authoritative.
- Client-side prediction, when later implemented, must be visual and reversible.
- Smoke checks in VS-001 assert that placeholders do not implement gameplay systems.
