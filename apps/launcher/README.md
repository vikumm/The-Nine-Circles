# Launcher

Minimal MMO-VS1 launcher bootstrap.

VS-005 adds OIDC Authorization Code with PKCE S256 using the system browser and a loopback callback. The launcher never receives a password, never creates a game session on its own and does not persist access, refresh or ID tokens.

Commands:

```bash
dotnet run --project apps/launcher/Divinity.Launcher.csproj -- status
dotnet run --project apps/launcher/Divinity.Launcher.csproj -- login
dotnet run --project apps/launcher/Divinity.Launcher.csproj -- logout
```

Local defaults target the VS-002 Keycloak realm:

- authority: `http://127.0.0.1:8080/realms/divinity-dev`;
- client: `divinity-launcher-dev`;
- callback: `http://127.0.0.1:{ephemeral}/callback`.

Game tickets, IPC, Unity startup, patching and gameplay behavior remain out of scope until later VS tasks.
