# ADR-0009: Concurrent Edit Boundaries

Status: Accepted

## Context

The GDD warns that two agents must not simultaneously alter `.proto`, migrations or shared game rules.

## Decision

Shared contracts, migrations and `packages/game-rules` require explicit coordination before concurrent edits.

## Consequences

- Agents keep implementation scoped to the active VS task.
- Shared behavior changes should be small, reviewed and covered by tests.
- Existing user or agent changes are preserved unless the user explicitly asks to revert them.
