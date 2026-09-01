# Platform API

This folder contains the Platform API service for MMO-VS1.

Current scope:

- compile as an ASP.NET Core service;
- expose `/healthz` for bootstrap smoke checks;
- issue VS-006 30-second game tickets through `POST /launcher/game-ticket`.

`POST /launcher/game-ticket` requires an authenticated account id. Development and test runs may enable `X-Divinity-Dev-Account-Id` only with `DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER=true`.

Out of scope here:

- long refresh sessions;
- WSS handshake;
- characters;
- inventory;
- economy;
- rewards;
- gameplay persistence.
