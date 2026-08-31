# Agent Guidance for MMO-VS1

## Source of Truth

`MMO_GDD_Tecnico_Vertical_Slice_v1.md` is the official scope, architecture, server authority, acceptance criteria, tests and Definition of Done for MMO-VS1. When code and the GDD disagree, stop and resolve the discrepancy before expanding scope.

## Scope

MMO-VS1 delivers the vertical slice described by the GDD through the ordered backlog VS-001 to VS-020. Do not skip gates. Do not start identity, movement, combat, loot, inventory or reconnect work before the required predecessor tasks are green.

## Server Authority

The server is authoritative for identity, sessions, movement, collision, combat, rewards, inventory, economy, checkpoints and persistence. Clients may send intents only. Clients must never decide final damage, XP, currency, loot, rarity, item instance ownership, cooldown completion, collision, spawn, position, death or reward grants.

## Client Boundary

Launcher and Unity client code may contain bootstrap, configuration, UI and reversible prediction only when the matching task permits it. No authoritative gameplay logic belongs in the client.

## Concurrent Editing Limits

Only one implementer should modify shared `.proto`, migrations or `packages/game-rules` at a time. If two agents need the same shared surface, coordinate first and keep changes scoped to the active VS task. Do not revert changes from another agent unless the user explicitly authorizes it.

## Definition of Done

A task is done only when behavior is implemented, build has no relevant new warnings, required unit/integration/smoke tests pass, authorization and input validation are present where applicable, structured logs contain no secrets, metrics are added where applicable, migrations have rollback strategy, protocol remains versioned, documentation is updated, balance values are not hardcoded outside content, failure/retry scenarios are tested and human review is complete.

## Required Verification

Before concluding any implementation task, run the relevant restore, build and test commands. For VS-001, the minimum local commands are:

```sh
dotnet restore Divinity.sln
dotnet build Divinity.sln --configuration Release --no-restore
dotnet run --project packages/test-fixtures/smoke-tests/Divinity.SmokeTests.csproj --configuration Release --no-build
```
