# ADR-0012: Protobuf v1 Contracts

Status: Accepted

## Context

VS-003 requires versioned Protobuf contracts, C# generation for client and server, envelope serialization tests, invalid input rejection and a minimal client-to-gateway `ClientHello` smoke path.

The task permits Google Protobuf/gRPC dependencies. No gameplay authority, balance, persistence, pathfinding, IA or UI should be implemented.

## Decision

Contracts live in `packages/contracts-proto` under Protobuf package `divinity.protocol.v1` and C# namespace `Divinity.Contracts.V1`.

The repository uses:

- `Google.Protobuf` 3.36.0 for runtime serialization/deserialization;
- `Grpc.Tools` 2.83.0 for build-time C# generation with `PrivateAssets=all`.

Dependency record:

| Package | Reason | License | Maintenance | Security impact |
|---|---|---|---|---|
| `Google.Protobuf` 3.36.0 | Required runtime for generated C# messages and Protobuf serialization tests. | BSD-3-Clause | Maintained by the Protocol Buffers project. | Parses untrusted network payloads; gateway enforces a 64 KiB limit and rejects malformed payloads. |
| `Grpc.Tools` 2.83.0 | Required build-time C# generation from `.proto`. | Apache-2.0 | Maintained by the gRPC project. | Build-time only with `PrivateAssets=all`; not shipped as runtime code by this package. |

The Game Gateway exposes only `POST /protocol/v1/client-hello` as a VS-003 smoke endpoint. It accepts a `ClientEnvelope` with `ClientHello`, enforces the 64 KiB limit and returns a controlled `ServerEnvelope`. It does not implement WSS, authentication, sessions, game ticket consumption or gameplay routing.

## Consequences

- C# contract generation is exercised by building the contracts package.
- Gateway and World Runtime compile against the generated C# types.
- Client messages remain intent-only and do not carry authoritative damage, XP, currency, rarity, item ownership, cooldown completion or death decisions.
- Future incompatible protocol changes must use a new package version.
