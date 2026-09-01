# Contracts Proto

This package contains the VS-003 protocol contracts and the VS-006 game-ticket primitives.

Scope:

- versioned Protobuf package `divinity.protocol.v1`;
- C# generation through the .NET build;
- short-lived game-ticket issue and consume models;
- client messages are intents only;
- no balance, AI, pathfinding, reward persistence or UI behavior.

The conceptual envelope size limit is 64 KiB. Gateways must reject larger payloads before expensive parsing.

Game-ticket secrets are opaque bearer values. Store and audit code must handle only ticket hashes or non-secret metadata after issue.
