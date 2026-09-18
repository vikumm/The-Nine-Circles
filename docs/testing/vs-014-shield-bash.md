# VS-014 Shield Bash Test Notes

Run the VS-014 Shield Bash gate from the repository root:

```bash
dotnet restore packages/test-fixtures/shield-bash-tests/Divinity.ShieldBashTests.csproj
dotnet build packages/test-fixtures/shield-bash-tests/Divinity.ShieldBashTests.csproj --configuration Release --no-restore
dotnet run --project packages/test-fixtures/shield-bash-tests/Divinity.ShieldBashTests.csproj --configuration Release --no-build
```

The VS-014 tests cover:

- 10 MP cost on valid hit;
- 5 second server-side cooldown;
- 1.25 second stun application and expiry;
- +1 skill XP only on valid hit;
- no skill XP for invalid action id, out-of-range cast or dead target;
- idempotency by `action_id`;
- rank 2 at 20 skill XP and max rank 2;
- rejection for invalid class;
- rejection for no MP, stunned attacker and out-of-range target.

Authority notes:

- the client sends only `CastIntent`;
- Gateway only routes the intent after `JoinWorld`;
- World Runtime validates MP, class, cooldown, target, range, stun and skill XP;
- `CombatEvent`, `SkillStateChanged` and `CharacterProgressed` are server-authored results.
