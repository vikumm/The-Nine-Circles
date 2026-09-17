# VS-012 Moss Slime AI Test Notes

Run the VS-012 monster AI gate from the repository root:

```bash
dotnet restore packages/test-fixtures/monster-ai-tests/Divinity.MonsterAiTests.csproj
dotnet build packages/test-fixtures/monster-ai-tests/Divinity.MonsterAiTests.csproj --configuration Release --no-restore
dotnet run --project packages/test-fixtures/monster-ai-tests/Divinity.MonsterAiTests.csproj --configuration Release --no-build
```

The VS-012 tests cover:

- Moss Slime spawns from the authoritative Training Field artifact inside a valid combat region;
- active Moss Slime count stays within the 30-instance cap;
- aggro detects a player within 6 units;
- leash beyond 10 units enters Return, regenerates HP and grants no reward;
- path recalculation stays capped at 4 per second;
- target loss when the player is dead;
- target loss when the player disconnects;
- respawn 8 seconds after valid death;
- preliminary in-range attack event through existing `CombatEvent`.

Protocol limitation:

- VS-012 does not modify contracts because `packages/contracts-proto` is outside the task's allowed files.
- The current protocol lacks `EntitySpawned` and `EntityDespawned`; monster presence is represented through `WorldSnapshot.EntityState` until a future contract task defines explicit lifecycle events.
