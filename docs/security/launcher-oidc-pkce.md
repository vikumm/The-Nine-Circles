# Launcher OIDC PKCE

VS-005 implements the launcher account login boundary.

## Scope

- OIDC Authorization Code with PKCE S256.
- System browser login.
- Local loopback callback on `127.0.0.1`.
- Token exchange with the configured identity provider.
- Minimal local login state without access, refresh or ID token persistence.
- Logout clears local state and opens the provider logout endpoint when discovery advertises it.

## Authority Boundary

The launcher authenticates the account only. It does not create a game session, issue a game ticket, consume a game ticket or authorize gameplay actions.

The launcher must not capture the user's password. Password entry belongs to the system browser and identity provider.

## Sensitive Data Rules

Do not log or persist:

- passwords;
- authorization codes;
- access tokens;
- refresh tokens;
- ID tokens;
- future game tickets or reconnect tokens.

The local state file stores only:

- OIDC authority;
- client id;
- authentication timestamp.

## Local Keycloak

Defaults:

- authority: `http://127.0.0.1:8080/realms/divinity-dev`;
- client id: `divinity-launcher-dev`;
- callback path: `/callback`;
- test user: `divinity.dev`.

The dev realm allows loopback redirect URI wildcards for dynamic callback ports. The launcher still accepts only the configured callback path and matching `state`.

The dev realm sets `sslRequired` to `none` so local HTTP loopback tests can complete. This is not a production setting.

The test user's password is a local-only development placeholder in `.env.example` and the imported Keycloak realm. The launcher itself never reads or asks for that password.

## Commands

```bash
dotnet run --project apps/launcher/Divinity.Launcher.csproj -- status
dotnet run --project apps/launcher/Divinity.Launcher.csproj -- login
dotnet run --project apps/launcher/Divinity.Launcher.csproj -- logout
```

Automated Keycloak login testing is enabled with `DIVINITY_RUN_KEYCLOAK_LOGIN_TEST=true`.
