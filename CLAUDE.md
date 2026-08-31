# Claude Guidance for MMO-VS1

Use `MMO_GDD_Tecnico_Vertical_Slice_v1.md` as the authoritative specification for MMO-VS1. Preserve the task order and gates in section 24.

## Operating Rules

- Implement only the active VS task and only in its allowed files.
- Keep server-side authority intact.
- Treat client messages as intents, not truth.
- Do not put authoritative movement, combat, inventory, reward, economy or persistence decisions in the launcher or Unity client.
- Coordinate before editing `.proto`, migrations or `packages/game-rules`.
- Do not add dependencies without recording reason, license, maintenance posture and security impact.

## Definition of Done

A task is complete only when the implementation matches the GDD, build passes without relevant new warnings, required tests pass, docs are updated, logs avoid secrets, input validation and authorization are present when applicable, versioned protocol is preserved, balance values stay in content, failure/retry behavior is tested and human review is complete.

## Verification

Run restore, build and the relevant tests before reporting completion. For VS-001, run:

```sh
dotnet restore Divinity.sln
dotnet build Divinity.sln --configuration Release --no-restore
dotnet run --project packages/test-fixtures/smoke-tests/Divinity.SmokeTests.csproj --configuration Release --no-build
```
