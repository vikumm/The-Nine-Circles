# ADR-0010: Task Gates and Definition of Done

Status: Accepted

## Context

The GDD defines gates per sprint and a Definition of Done in section 25.

## Decision

No later VS task starts until the active task has met its acceptance criteria and required tests, and the user authorizes the next implementation step.

## Consequences

- VS-002 does not start automatically after VS-001.
- Failed gates block scope expansion.
- Completion reports must list commands, changed files, limitations, acceptance status and remaining errors.
