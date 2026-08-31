# Contracts Proto

This package contains the VS-003 protocol contracts.

Scope:

- versioned Protobuf package `divinity.protocol.v1`;
- C# generation through the .NET build;
- client messages are intents only;
- no balance, AI, pathfinding, reward persistence or UI behavior.

The conceptual envelope size limit is 64 KiB. Gateways must reject larger payloads before expensive parsing.
