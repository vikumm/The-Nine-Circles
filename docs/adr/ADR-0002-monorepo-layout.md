# ADR-0002: Monorepo Layout

Status: Accepted

## Context

The GDD section 22 defines the initial repository layout for applications, services, shared packages, content, tools, infrastructure and docs.

## Decision

The repository uses the section 22 layout. VS-001 creates only the paths allowed by its task definition.

## Consequences

- Current source projects live under `apps`, `services` and `packages`.
- Content, tools and infrastructure folders are deferred until their VS tasks allow edits.
- Future changes should preserve this layout unless a new ADR changes it.
