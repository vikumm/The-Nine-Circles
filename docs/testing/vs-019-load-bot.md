# VS-019 Load Bot

## Scope

VS-019 adds a non-rendering load bot for the MMO-VS1 critical path. The bot runs in process against the server-side services used by the vertical slice and exercises:

1. test identity;
2. game ticket issue and consume;
3. `ClientHello`;
4. `JoinWorld`;
5. `MoveIntent`;
6. `AttackIntent`;
7. `CastIntent` for `knight_shield_bash_r1`;
8. monster death;
9. `RewardGranted`;
10. `InventoryDelta` and OffHand equip;
11. disconnect;
12. `ReconnectRequest`;
13. state verification after reconnect.

The tool does not render Unity, run PvP, or benchmark final production capacity.

## Commands

Smoke profile:

```bash
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release -- --profile smoke
```

Acceptance-shaped profile:

```bash
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release -- --profile acceptance
```

Custom ramp:

```bash
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release -- --ramp 1,25,50,100 --combat-bots 50 --resilience-bots 100 --disconnect-percent 20
```

Fixture gate:

```bash
dotnet run --project packages/test-fixtures/load-bot-tests/Divinity.LoadBotTests.csproj --configuration Release
```

## Report

The final report includes:

- overall success/failure;
- ramp configuration;
- per-bot success/failure;
- latency summary by operation;
- message counts by protocol type;
- duplicate reward count;
- inconsistencies.

`--report path/to/report.json` writes the same report as JSON.

## Isolation

The bot stores all test data under an OS temp path and deletes it when each run completes. Deterministic combat and loot random sources are injected only inside the load bot process; production balance defaults remain unchanged.

Reward transaction and inventory/equipment persistence are still separate stores in the current vertical slice. The bot validates both server-side paths without writing client-authoritative state or pretending that a production inventory bridge already exists.
