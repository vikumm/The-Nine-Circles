# ADR-0017: VS-008 Knight Creation and Selection

## Context

VS-008 must allow an authenticated account to create or select one Knight, persist it across service restart and enter the world with server-defined stats and safe spawn.

VS-009 map import, movement, combat, inventory and reconnect are not in scope.

## Decision

Add server-side character rules to `packages/game-rules`. This package owns the VS-008 Knight catalog, name normalization/validation and one-slot creation semantics.

Platform API exposes `POST /characters/knight` and `GET /characters/knight`. The request accepts only the desired name. The server assigns vocation, stats, map id, channel id, content hash and safe spawn.

Use a file-backed `FileCharacterStore` selected by `DIVINITY_CHARACTER_STORE_PATH` for the local vertical-slice gate. The store writes a JSON document for `accounts_projection`/`characters`-equivalent state and uses an exclusive lock file for atomic name/slot checks.

Game Gateway verifies `JoinWorld.character_id` against the persisted character for the authenticated ticket account before acquiring the session lease. Accepted joins return `JoinAccepted` and a minimal `WorldSnapshot` from server-side values.

## Consequences

The client cannot choose class, stats, HP/MP, authoritative position, map authority or account ownership.

The local store proves persistence-after-restart and duplicate-name protection without adding new dependencies. A later database-backed store should replace it when the GDD task requires durable PostgreSQL migrations.

Only Knight and one character slot are supported until a later task expands vocation/character selection.
