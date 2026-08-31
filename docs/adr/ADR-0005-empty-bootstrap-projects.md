# ADR-0005: Empty Bootstrap Projects

Status: Accepted

## Context

VS-001 requires projects that compile without implementing identity, handshake, movement, combat, persistence, inventory or launcher completion.

## Decision

VS-001 creates minimal executable or library projects with explicit metadata and no gameplay behavior.

## Consequences

- Services expose only bootstrap health or console output.
- Later VS tasks can expand projects in place.
- Smoke tests verify the placeholders stay inside VS-001 scope.
