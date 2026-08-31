# Game Gateway Bootstrap

This folder contains the VS-001 Game Gateway placeholder.

Scope for VS-001:

- compile as an ASP.NET Core service;
- expose `/healthz` for bootstrap smoke checks;
- do not implement WSS, tickets, sessions, leases, rate limits or gameplay routing.

Future work:

- VS-003 defines protocol contracts.
- VS-007 adds authenticated WSS handshake and session lease behavior.
