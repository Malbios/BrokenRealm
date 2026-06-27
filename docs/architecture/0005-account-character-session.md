# ADR 0005: Account, character, and session boundaries

Status: Accepted

## Context

BrokenRealm already stores durable character progress separately from world objects and behavior source. Commands execute against an explicit character ID. Before authentication is added, the kernel still hardcoded the seeded `prototype-player` at the HTTP boundary.

Accounts identify people. Characters own durable game progress. Sessions authenticate a temporary browser connection and select which owned character is acting. These lifecycles differ and must not collapse into one serialized game record. ADR 0004 reserved this split; this decision defines the runtime model used until authentication is implemented.

## Decision

### Durable accounts

Persist account records separately from session state:

- stable account ID
- optional display name

Accounts do not store location, inventory, or other world progress directly. Ownership is expressed through each character's `AccountId`.

Seeded development data uses `prototype-account`, which owns the seeded player characters until real account creation exists.

### Durable characters

Persist character records with:

- stable character ID
- owning `AccountId`
- location
- inventory
- character revision metadata managed by the storage adapter

Commands always execute as a character. Character records never embed session identifiers.

### Ephemeral sessions

Sessions are process-local operational state and are not written to world snapshots.

Each session stores:

- stable session ID
- bound account ID
- selected character ID
- creation and last-seen timestamps

Until authentication exists, new browser sessions bind to `prototype-account`. Future authenticated sessions will bind to the signed-in account instead.

Character selection is validated on every change: the selected character must exist and its `AccountId` must match the session account.

### HTTP boundary

Player endpoints resolve the acting character through the session layer:

- `GET /game/session` returns the bound account, selected character, and owned character summaries
- `POST /game/session/character` changes the selected owned character
- `POST /game/command` executes as the session's selected character

Sessions are identified by an HttpOnly cookie (`brokenrealm_session`). Admin behavior endpoints remain outside the session model for now.

### Snapshot format

Storage format version 2 adds an `accounts` section and `accountId` on each persisted character. Loading format version 1 snapshots migrates every character to `prototype-account` and synthesizes that account record.

## Consequences

- Multiple characters can exist under one account before authentication is added.
- Adding login later replaces only the session account-binding step; command dispatch and persistence boundaries stay the same.
- World snapshots remain free of session cookies, selected-character caches, and connection presence.
- Character switching is explicit and validated instead of implicit singleton-player assumptions.

## Implementation status

Implemented:

- account and character domain types with explicit ownership
- snapshot format version 2 with v1 migration
- process-local session store and cookie-backed session selection
- player session endpoints and command dispatch through selected character
- browser character selector when an account owns multiple characters

Not yet implemented:

- durable session storage for multi-server deployment
- account administration beyond seeded prototype data and self-service registration

Implemented since this ADR was written:

- password hashing, login, logout, and registration endpoints
- guest sessions that continue to bind to `prototype-account` until login