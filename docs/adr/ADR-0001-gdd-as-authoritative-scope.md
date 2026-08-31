# ADR-0001: GDD as Authoritative Scope

Status: Accepted

## Context

MMO-VS1 has an official technical GDD with explicit scope, gates, architecture, backlog and Definition of Done.

## Decision

`MMO_GDD_Tecnico_Vertical_Slice_v1.md` is the source of truth for implementation scope and ordering.

## Consequences

- Work must follow VS-001 through VS-020 in order.
- Conflicts between implementation and GDD must be resolved before expanding scope.
- Features outside the current task remain out of scope even if they are easy to add.
