# Game Rules

This package contains server-side rules and versioned catalog values for MMO-VS1.

Current scope:

- provide the VS-008 Knight level 1 catalog;
- validate and normalize character names;
- create/select exactly one Knight slot per account;
- keep initial stats and safe spawn server-side;
- provide VS-010 shared movement normalization and cardinal-facing helpers;
- provide VS-011 visual-only client prediction and reconciliation helpers.

Out of scope here:

- four vocations;
- combat;
- inventory;
- rewards.

Unity consumes `Movement/` as a local package through `apps/game-client-unity/Packages/manifest.json`. Prediction helpers are reversible visual state only; server snapshots and corrections remain authoritative.
