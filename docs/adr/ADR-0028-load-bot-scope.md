# ADR-0028: VS-019 Load Bot Scope

## Context

VS-019 requires a non-rendering load bot that proves the vertical slice critical path without adding Unity rendering, PvP, external environments, or final production benchmarking. The previous gates already established contracts, session authority, movement, combat, rewards, inventory/equipment, and reconnect.

## Decision

Create `tools/load-bots` as a .NET console tool that runs deterministic in-process scenarios against the existing server-side services. The tool issues and consumes real game tickets, acquires session leases, joins the world, moves, attacks, casts Shield Bash, kills a Moss Slime, validates reward persistence, equips an OffHand item through the inventory service, disconnects, reconnects, and verifies state.

The acceptance-shaped profile exposes ramp `1 -> 25 -> 50 -> 100`, 50 concurrent combat bots, and 20% batch disconnect/reconnect. CI keeps the smoke profile and fixture gate so the main pipeline stays fast and stable.

No protocol changes are introduced for VS-019.

## Consequences

The bot remains isolated from Unity and external infrastructure, which makes it reliable in CI. Deterministic random sources are confined to the load bot and test fixtures, so production balance is not changed.

The current vertical slice still has separate reward and inventory/equipment stores. The bot validates both authoritative server paths but does not invent a client-side or ad hoc production bridge between them.
