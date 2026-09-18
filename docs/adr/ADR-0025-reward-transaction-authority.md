# ADR-0025: VS-016 Reward Transaction Authority

## Context

VS-016 requires reward grants for valid monster deaths to be transactional and idempotent by `reward_key = kill_id + character_id`. The transaction must include reward grant, currency ledger, item instance, inventory slot or pending reward, XP/skill XP and outbox event.

The complete inventory/equipment workflow is still assigned to VS-017, and gateway delivery is outside the VS-016 allowed file list.

## Decision

World Runtime owns reward transaction authority. A file-backed reward store uses an exclusive lock and writes a single transaction document containing `reward_grants`, `currency_ledger`, `wallets`, `item_instances`, `inventory_slots`, `pending_rewards`, character reward progress, `outbox_events` and audit events.

`reward_key` is checked before mutation. If the same key is retried, the runtime returns the existing result and does not create extra ledger rows or item instances.

Loot rolling is server-side. The VS-016 reward catalog provides deterministic injection for tests, covering training currency, small potion, Wooden Shield Normal, Good and Rare outcomes.

## Consequences

- Clients never request or confirm rewards.
- Currency ledger records are append-only.
- Inventory-full rewards create `pending_reward` and still grant currency.
- Outbox records are persisted with the committed reward document and are not published before commit.
- Gateway delivery and final inventory manipulation remain later work.
