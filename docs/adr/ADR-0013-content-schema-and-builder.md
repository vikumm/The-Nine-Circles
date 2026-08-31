# ADR-0013: Content Schema and Builder

## Context

VS-004 requires a versioned content schema and a minimal pipeline that validates map, skill, item and loot table source files before generating separate artifacts for Unity and server runtime use.

The GDD requires server-side authority. The client may receive visual data, but it must not define walls, spawns, safe zones, loot, rarity or final item/skill attributes.

## Decision

Create `packages/content-schema` as a .NET library with typed JSON models, deterministic validation and canonical SHA-256 content hashing.

Create `tools/content-builder` as a minimal .NET CLI that reads the source content tree, validates it and generates:

- `unity/<mapId>.visual.json`;
- `server/<mapId>.authoritative.json`.

Both artifacts include the same `contentVersion` and `contentHash`. The server artifact keeps the authoritative content data. The Unity artifact is a visual/read-only projection for bootstrap and inspection.

No external schema dependency is introduced in VS-004. Validation is implemented in C# using the standard .NET runtime so the gate stays small and deterministic.

## Consequences

Content changes now have a CI gate before they can be consumed by client or server work.

Future tasks can add richer schemas or generated contract IDs without moving authority into the client.

The custom validator must remain strict enough to reject invalid fixtures. If it becomes too broad or permissive, later Unity/server divergence can slip through CI.

## Unresolved

The GDD has not yet defined the final Unity import path, remote chunk strategy or visual editor workflow. Generated artifacts therefore live under the builder output path until a later task assigns runtime-specific integration paths.
