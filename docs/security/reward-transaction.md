# Reward Transaction Security Notes

VS-016 rewards are server-authored. The client must never request, approve or confirm XP, currency, item instances, rarity, inventory slot, pending reward or `reward_key`.

Security properties:

- `reward_key = kill_id + character_id` is the idempotency boundary;
- duplicate reward keys return the existing grant without appending ledger rows or creating item instances;
- currency ledger rows are append-only;
- item instances record owner and location as inventory or pending reward;
- outbox events are stored only inside the committed reward document;
- audit events contain operational identifiers, not full sensitive payloads.

Operational rollback:

- stop new reward grants;
- keep existing `reward_grants`, ledger rows and audit events for investigation;
- reprocess only verified idempotency keys.
