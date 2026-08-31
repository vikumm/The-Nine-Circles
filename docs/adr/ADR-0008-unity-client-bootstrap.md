# ADR-0008: Unity Client Bootstrap

Status: Provisional

## Context

The GDD requires a Unity client, but VS-001 does not permit gameplay or networking. Unity Hub is installed locally with Unity 2021.3.26f1 available.

## Decision

VS-001 creates a minimal Unity project shell pinned to Unity 2021.3.26f1 with no scripts, scenes or gameplay assets.

## Consequences

- The folder can be opened by Unity for later tasks.
- No client-authoritative code exists in VS-001.
- Exact production Unity version and packages remain undefined until a future task records them.
