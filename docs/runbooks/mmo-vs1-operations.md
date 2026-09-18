# MMO-VS1 Operations Runbook

## Setup

1. Install .NET 9 SDK, Docker Desktop and Unity 2021.3.26f1.
2. From the repository root, validate local services:

```bash
docker compose -f infra/compose/compose.yml config
docker compose -f infra/compose/compose.yml --profile observability up -d
```

3. Restore and build:

```bash
dotnet restore Divinity.sln
dotnet build Divinity.sln --configuration Release --no-restore
```

## Execution

Run the vertical slice automated gate:

```bash
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release --no-build -- --profile acceptance
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release --no-build -- --profile hardening --soak-duration-seconds 1
```

For release approval, run:

```bash
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release --no-build -- --profile soak
```

## Load

The acceptance profile ramps 1 -> 25 -> 50 -> 100 bots, keeps 50 simultaneous combat bots and verifies reconnect/reward/inventory consistency. The hardening profile adds offensive/defensive protocol and authority cases.

Watch for failed checks, duplicate reward records, reconnect failures, rate limit regressions and latency spikes in the generated report.

## Observability

Start the observability profile before manual investigation:

```bash
docker compose -f infra/compose/compose.yml --profile observability up -d otel-collector prometheus grafana
```

Expected signal names include:

- `divinity.platform.game_tickets`;
- `divinity.platform.characters`;
- `divinity.gateway.messages`;
- `divinity.gateway.rate_limits`;
- `divinity.gateway.authentications`;
- `divinity.gateway.disconnects`;
- `divinity.world.movement_corrections`;
- `divinity.world.damage_rejected`;
- `divinity.world.monster_deaths`;
- `divinity.world.rewards_granted`;
- `divinity.world.reward_duplicates_blocked`.

Expected trace names include:

- `divinity.platform.game_ticket`;
- `divinity.gateway.auth_ticket_join`;
- `divinity.gateway.move_intent`;
- `divinity.gateway.attack_intent`;
- `divinity.gateway.cast_intent`;
- `divinity.gateway.equip_validation_persistence`;
- `divinity.gateway.disconnect_checkpoint_reconnect`;
- `divinity.world.kill_reward_inventory_delta`.

## Death And Loot Investigation

1. Locate the character id and account pseudonym in structured logs.
2. Check `CombatEvent.kill_id` and reward key for a single matching reward grant.
3. Verify that duplicate reward checks report zero duplicated reward records.
4. Verify `InventoryDelta.inventory_version` progressed once for the drop/equipment operation.
5. Confirm reconnect did not replay old `RewardGranted` payloads as authority.

Never use client logs as source of truth for death, loot, currency, inventory or equipment.

## Windows QA Profiling

Run the Unity profiling bootstrap on the Windows QA machine:

```bash
Unity.exe -batchmode -quit -projectPath apps/game-client-unity -executeMethod Divinity.Editor.DivinityQaProfileSmokeTest.Run
```

The release sign-off must record 1920x1080, 60 FPS target, memory trend and GC spike observations.

## Rollback

Rollback only the smallest hardening or observability change that caused the regression. Keep CI gates enabled. If rollback affects authority, persistence, security or protocol behavior, add a follow-up ADR before marking the vertical slice approved again.
