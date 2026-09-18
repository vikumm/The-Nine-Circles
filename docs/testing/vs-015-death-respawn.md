# VS-015 Death And Respawn Test Notes

Status: implemented for the MMO-VS1 vertical slice gate.

Mandatory checks covered by `packages/test-fixtures/death-respawn-tests`:

- character HP zero enters `Dead`;
- death-screen status effect lasts 5 seconds;
- respawn occurs at Safe Spawn;
- respawn stores a Safe Spawn checkpoint;
- each durable equipped item loses 1 durability on death;
- zero-durability equipment remains equipped and contributes no attributes;
- valid monster death emits a unique `kill_id`;
- delayed attacks against a dead monster do not duplicate death or Shield Bash skill XP;
- disconnect during death does not duplicate the actor on join retry.

Commands:

```bash
dotnet restore packages/test-fixtures/death-respawn-tests/Divinity.DeathRespawnTests.csproj
dotnet build packages/test-fixtures/death-respawn-tests/Divinity.DeathRespawnTests.csproj --configuration Release --no-restore
dotnet run --project packages/test-fixtures/death-respawn-tests/Divinity.DeathRespawnTests.csproj --configuration Release --no-build
```

Out of scope:

- public repair;
- XP loss;
- final loot;
- pending reward;
- final death animations;
- full durable item persistence and reconnect replay, which remain later GDD work.
