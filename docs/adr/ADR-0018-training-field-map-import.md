# ADR-0018: VS-009 Training Field Map Import

## Context

VS-009 requires the Training Field map to be imported through the content pipeline, producing a Unity visual artifact and a server authoritative artifact from the same source data and content hash.

The GDD requires the server to own bounds, collision, safe spawn and join acceptance. Unity may load a visual projection, but it must not become the source of authoritative collision or spawn decisions.

## Decision

Extend the VS-004 map schema and builder to validate the 96x96 Training Field map, the `map_training_field_01` stable id, 16x16 generated chunks, safe spawn, movement corridor, combat zone, leash area, collision wall and equipment point.

Generate chunks in the content artifacts instead of storing them manually in source JSON. This keeps Unity and server artifacts derived from the same validated map source.

Add `WorldMapCatalog` and `WorldJoinMapValidator` to `services/world-runtime`. The runtime loads the server authoritative artifact and rejects map join validation when the client content hash differs from the authoritative content hash.

## Consequences

Content drift between Unity and server is now covered by tests that compare artifact hashes and chunk layout.

Collision-wall source data is validated against blocked cells, so a visual wall cannot exist without corresponding authoritative collision data.

The current VS-009 allowed file list does not include `services/game-gateway`, so WSS integration of the hash rejection is intentionally not wired in this task. The validation exists in `world-runtime` and should be connected to the gateway only when a later allowed gate includes that path.

## Unresolved

The final Tiled import format, remote map streaming and production artifact distribution path are not defined in the GDD for this task.
