# ADR-0023: VS-014 Shield Bash Control Authority

## Context

VS-014 adds `knight_shield_bash_r1` as the first control skill. The GDD requires server validation for class, MP, cooldown, range, target state, stun and skill XP, while the client continues to send intent only.

The protocol already has `CastIntent` and `CombatEvent`. VS-014 needs server-authored skill progression feedback and a controlled rejection code for failed casts.

## Decision

Keep `CastIntent` intent-only: skill id, target entity id and action id. The client cannot send MP cost, final MP, cooldown result, stun duration, damage, target HP, skill XP or rank.

Add `CharacterProgressed` to Protocol v1 as a backward-compatible server payload and add `ERROR_CODE_CAST_REJECTED = 25` for controlled cast rejection.

Implement Shield Bash catalog values in `packages/game-rules/Combat`: 1.4 unit range, 5 second cooldown, 10 MP cost, 1.2 coefficient, base power 8, 1.25 second stun, +1 skill XP per valid hit, max rank 2 and rank 2 at 20 XP.

World Runtime owns all cast validation, resource mutation, stun application, cooldown state and skill XP mutation. Gateway only routes `CastIntent` after `JoinWorld` and emits the server payloads returned by World Runtime.

## Consequences

Retry safety depends on `action_id`; duplicate Shield Bash actions are rejected before granting additional skill XP.

Skill resource and progression state remain in-memory for the vertical slice. Durable domain persistence is still out of scope for VS-014.

The source `content/skills/knight/knight_shield_bash_r1.json` remains the content-side definition. Runtime balance is mirrored in `ShieldBashCatalog` until later content/runtime catalog reconciliation work is defined.

## Unresolved

The GDD does not yet define persistence shape for `character_skills`, MP regeneration, client VFX timing, PvP behavior or broad reward flow. Those remain future decisions.
