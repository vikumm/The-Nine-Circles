# ADR-0014: Launcher PKCE Login

## Context

VS-005 requires the launcher to authenticate accounts through OIDC Authorization Code with PKCE, using the system browser and a loopback callback. The launcher must not capture passwords, must not create game sessions and must not leak tokens through logs, configuration files, persisted URLs or process arguments.

## Decision

Implement a minimal .NET launcher auth flow with:

- OIDC discovery;
- PKCE S256 `code_verifier` and `code_challenge`;
- cryptographically random `state` and `nonce`;
- `127.0.0.1` loopback callback validation;
- authorization-code token exchange;
- local login state that persists only authority, client id and timestamp;
- logout that clears local state and opens the provider logout endpoint when available.

No external OIDC client dependency is added in VS-005. The implementation uses the .NET standard library to keep the bootstrap small and auditable.

The local Keycloak realm allows loopback redirect wildcard paths so Authorization Code can use a per-login ephemeral callback port. The launcher compensates by accepting only the configured callback path and matching `state`.

The local Keycloak realm sets `sslRequired` to `none` for VS-005 because the vertical-slice dev loopback flow runs over local HTTP. This is explicitly not a production identity-provider setting.

The existing `LauncherInfo.ImplementsLogin` flag remains unchanged because the VS-005 allowed file set does not include the older VS-001 smoke-test package that asserts the bootstrap placeholder contract. A VS-005-specific `SupportsPkceLogin` flag records the new capability.

## Consequences

The launcher can prove account authentication without owning passwords or game-session authority.

Access, refresh and ID tokens are kept in memory for the active process only. Later tasks that require durable secure token storage must introduce an OS-backed credential store decision before persisting secrets.

The Keycloak integration test uses a local development user from the imported realm. This keeps the launcher password-free while allowing CI to exercise a browser-style Authorization Code flow.

## Unresolved

The final launcher UI framework and durable OS credential-store strategy are not defined by the GDD yet.

Game tickets, IPC, WSS join and character selection remain for VS-006 and later.
