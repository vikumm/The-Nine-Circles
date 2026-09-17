# VS-013 Basic Attack Test Notes

Run the VS-013 basic attack gate from the repository root:

```bash
dotnet restore packages/test-fixtures/basic-attack-tests/Divinity.BasicAttackTests.csproj
dotnet build packages/test-fixtures/basic-attack-tests/Divinity.BasicAttackTests.csproj --configuration Release --no-restore
dotnet run --project packages/test-fixtures/basic-attack-tests/Divinity.BasicAttackTests.csproj --configuration Release --no-build
```

The VS-013 tests cover:

- damage formula using attack coefficient 1.0 and base power 2;
- defense mitigation;
- deterministic variance injection for tests;
- critical hit multiplier;
- minimum damage of 1;
- valid and invalid 1.5-unit range;
- server-side cooldown enforcement;
- dead target rejection;
- missing target rejection;
- other-map target rejection;
- repeated attack sequence rejection;
- stunned attacker rejection.

Authority notes:

- the client sends only `AttackIntent`;
- Gateway only routes the intent after `JoinWorld`;
- World Runtime validates and calculates outcome;
- `CombatEvent` carries the animation-facing result;
- `SkillStateChanged` exists for cooldown state synchronization.
