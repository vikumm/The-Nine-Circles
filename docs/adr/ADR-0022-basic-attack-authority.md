# ADR-0022: VS-013 Basic Attack Authority

## Context

VS-013 adds `knight_basic_slash` as the first player attack. The GDD requires client intent only, server validation of target, range, cooldown, state and sequence, server-side RNG, damage calculation, HP update and `CombatEvent`.

The current protocol already has `AttackIntent` and `CombatEvent`, but did not expose a cooldown state event or a dedicated attack rejection code.

## Decision

Keep `AttackIntent` intent-only: target entity, skill id and action id. The client still cannot send damage, critical result, target HP, cooldown completion or death outcome.

Add `SkillStateChanged` to Protocol v1 as a backward-compatible server payload and add `ERROR_CODE_ATTACK_REJECTED = 24` for controlled attack rejection.

Implement Basic Slash balance and damage formula in `packages/game-rules/Combat`. World Runtime owns attack validation and damage resolution through `WorldCombatRuntime`, using authoritative actor state from movement and authoritative monster state from the VS-012 monster runtime.

Gateway routes `AttackIntent` after `JoinWorld`; it does not calculate range, damage, cooldown, crit or HP locally.

## Consequences

Server tests can inject deterministic combat RNG for variance and critical checks. Production defaults use server-side randomness.

VS-013 supports only Basic Slash against Moss Slime entities. XP, loot, combos, Shield Bash and final animation wiring remain later tasks.

The source `content/skills/knight/knight_basic_slash.json` still belongs to the content pipeline. VS-013 runtime balance is held in `BasicSlashCatalog` to avoid regenerating content artifacts outside the task's allowed files.

## Unresolved

Future work must reconcile content skill data and generated artifacts with runtime combat catalogs, then decide whether `SkillStateChanged` is emitted on every accepted attack or only when the client needs cooldown resync.
