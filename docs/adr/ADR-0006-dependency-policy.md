# ADR-0006: Dependency Policy

Status: Accepted

## Context

The GDD lists allowed dependencies and requires any new dependency to record reason, license, maintenance and security impact.

## Decision

VS-001 adds no external runtime dependencies. Projects use only the .NET SDK and ASP.NET Core shared framework.

## Consequences

- Restore is fast and does not depend on gameplay packages.
- Future dependencies must be justified in ADRs before use.
- Test coverage in VS-001 uses a small console smoke runner instead of adding a test framework package.
