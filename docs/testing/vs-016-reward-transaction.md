# VS-016 Reward Transaction Test Notes

Status: implemented for the MMO-VS1 vertical slice gate.

Mandatory checks covered by `packages/test-fixtures/reward-transaction-tests`:

- valid monster death grants reward;
- idempotency by `reward_key`;
- retry after injected transient failure;
- injected deadlock retry without duplicate grant;
- inventory full sends item to `pending_reward` while currency is still granted;
- currency ledger is append-only;
- outbox event exists with the committed reward;
- deterministic QA loot covers small potion and Wooden Shield Normal, Good and Rare.

Commands:

```bash
dotnet build packages/test-fixtures/reward-transaction-tests/Divinity.RewardTransactionTests.csproj --configuration Release
dotnet run --project packages/test-fixtures/reward-transaction-tests/Divinity.RewardTransactionTests.csproj --configuration Release --no-build
```

Out of scope:

- trade;
- marketplace;
- public ground loot;
- party loot;
- crafting;
- gateway delivery of reward payloads.
