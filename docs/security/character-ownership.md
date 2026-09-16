# Character Ownership Security

Status: VS-008.

## Scope

VS-008 creates and selects exactly one Knight per authenticated account and lets that Knight enter the world through the Gateway.

It does not implement four vocations, movement, combat, inventory, reconnect or a final selection UI.

## Authority

The server controls:

- character id;
- account ownership;
- vocation;
- level;
- HP and MP;
- initial stats;
- map id;
- channel id;
- safe-spawn position;
- content hash.

The client sends only a requested name at creation time and a `character_id` intent at join time.

## Name Rules

Names are trimmed, whitespace-collapsed, accent-normalized and compared case-insensitively through a normalized key.

The normalized name is unique globally. A single account may create only one VS-008 Knight slot.

## Persistence

The local vertical-slice gate uses `DIVINITY_CHARACTER_STORE_PATH` and a file-backed JSON store with an exclusive lock file. This preserves created characters across service restarts in local tests and prevents duplicate name reservation races inside the file-backed store.

This is not the final distributed PostgreSQL model.
