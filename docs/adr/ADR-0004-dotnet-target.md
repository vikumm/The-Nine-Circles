# ADR-0004: .NET Target for VS-001 Bootstrap

Status: Provisional

## Context

The GDD permits ASP.NET Core/.NET 10. The local environment currently exposes .NET SDK 9.0.109 and no .NET 10 SDK. VS-001 acceptance requires local restore, build and smoke test execution.

## Decision

VS-001 bootstrap projects target `net9.0` so the foundation can compile and be tested in the available environment.

## Consequences

- This is an environment limitation, not a gameplay or architecture decision.
- Upgrading to .NET 10 remains required once the SDK is available.
- A future task or ADR must update target frameworks and CI together.
