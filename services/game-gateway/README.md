# Game Gateway

This folder contains the Game Gateway service for MMO-VS1.

Current scope:

- compile as an ASP.NET Core service;
- expose `/healthz` for bootstrap smoke checks;
- validate Protobuf v1 `ClientEnvelope` messages;
- consume VS-006 game tickets from `ClientHello.game_ticket`.

Out of scope here:

- authenticated WSS sessions;
- reconnect leases;
- gameplay routing;
- movement;
- combat;
- inventory.
