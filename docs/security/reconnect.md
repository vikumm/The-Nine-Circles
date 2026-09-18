# VS-018 Reconnect Security Notes

Reconnect uses a short-lived server token in addition to a fresh `ClientHello` game ticket. The reconnect token is not an account credential and must not be logged, placed in URLs or stored in plaintext server records.

Controls:

- token TTL is 30 seconds;
- token is hashed before persistence;
- successful reconnect consumes the old token and issues a new token;
- reconnect validates authenticated account id against the token and lease;
- `previous_connection_id` must match the token lineage;
- replayed or expired tokens are rejected;
- gateway transfers the actor lease explicitly so two actors cannot exist for one character.

The client may cache the reconnect token only long enough to survive a short transport drop. It must not replay old rewards, inventory deltas or snapshots as authoritative state.
