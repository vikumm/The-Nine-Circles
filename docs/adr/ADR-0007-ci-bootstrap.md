# ADR-0007: CI Bootstrap

Status: Accepted

## Context

Sprint 0 requires build and automated smoke tests on each commit.

## Decision

The initial GitHub Actions workflow restores `Divinity.sln`, builds it in Release and runs the VS-001 smoke runner.

## Consequences

- CI proves the bootstrap compiles and smoke checks execute.
- Full unit and integration suites are deferred until tasks add behavior that requires them.
- The workflow currently installs .NET 9 because of ADR-0004.
