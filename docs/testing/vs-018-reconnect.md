# VS-018 Reconnect Test Notes

Status: implemented for the MMO-VS1 vertical slice gate.

Mandatory checks covered by `packages/test-fixtures/reconnect-tests`:

- reconnect within 30 seconds;
- reconnect during combat keeps the actor and current HP;
- reconnect after expiration saves checkpoint and exits the map;
- two competing reconnects have one owner;
- `RewardGranted` before drop is not duplicated;
- unsafe checkpoint converts to Safe Spawn;
- inventory/equipment persist after drop;
- gateway session restart preserves reconnect token/lease.

Commands:

```bash
dotnet build packages/test-fixtures/reconnect-tests/Divinity.ReconnectTests.csproj --configuration Release
dotnet run --project packages/test-fixtures/reconnect-tests/Divinity.ReconnectTests.csproj --configuration Release --no-build
```

Out of scope:

- channel migration;
- shard transfer;
- login queue;
- long offline reconnect;
- rollback of progress.
