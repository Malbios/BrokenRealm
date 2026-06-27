# ADR 0008: Offline character limbo

Status: Accepted

## Context

BrokenRealm is singleplayer-first. Optional co-presence lets connected players see each other in rooms through room-scoped broadcasts and SignalR delivery. Today, a disconnected character remains a permanent player object with a `LocationId`, so they still appear in `look` contents and remain eligible as room-broadcast recipients once reconnected.

That shape is wrong for a game that treats the live world as something you enter while connected. Offline bodies would imply AFK presence, invite queued room-message designs, and couple timed world systems to connection state before those systems exist.

ADR 0005 separates durable characters from ephemeral sessions. ADR 0006 places playable characters in the object database with a location. This ADR defines how disconnect and reconnect change that location membership without collapsing account, character, or session boundaries.

## Decision

### Limbo is explicit non-presence

When a session ends or a player disconnects without an active session, the character enters **limbo**:

- `LocationId` is cleared (`None`). The character is not contained by any room or object.
- The character object remains in `GameState.Objects` with inventory, properties, and durable progress intact.
- Limbo characters are excluded from `look` / `location.contents` derivation and from `RoomBroadcast` recipient sets.
- SignalR room membership follows the session layer; disconnected clients leave character groups and stop receiving `roomLine` pushes.

Limbo is not a special room ID. It is absence from the containment graph.

### No offline simulation

Characters in limbo do not participate in time-based progression. Hunger, decay, timed effects, and similar systems must ignore limbo characters until an explicit re-entry path runs. This keeps singleplayer pacing authoritative to the connected player rather than wall-clock AFK time.

### Re-entry is explicit

Reconnecting creates or resumes a session, selects a character, and runs an explicit **enter play** step before commands dispatch against a located character. That step assigns a valid `LocationId` (initially the character's last safe location or a defined starter room policy).

Reconnect must not silently restore room presence from stale online state. Session selection and re-entry are separate from merely reloading durable character data.

### Room messages are live-only

Room-scoped messages (keys ending in `.room`) are delivered only to other connected players currently located in the target room. Offline characters do not accumulate a backlog of room lines. Optional co-presence is ephemeral observation, not a mailbox.

### Persistence

Limbo is represented in snapshots by a missing `LocationId` on the player object. No separate limbo table or queue is introduced. JSON snapshot persistence remains sufficient for this state.

## Consequences

- Disconnect removes social presence without deleting character progress.
- Room enter/leave broadcasts (`move.leave.room`, `move.arrive.room`) target only connected players in the explicit `roomId`; limbo characters are never recipients.
- Timed systems can key off `LocationId = Some _` as "in the live world" when they are added later.
- Implementing limbo requires session disconnect hooks, re-entry validation in command dispatch, and tests for look/broadcast exclusion—not a persistence redesign.

## Implementation status

Implemented:

- `LocationId = None` limbo state with persisted `lastSafeLocationId` on the player object
- limbo entry on logout, last SignalR disconnect, and character switch when the previous character has no live connection
- `POST /game/session/enter` re-entry that restores the last safe location (default `forest`) and emits `move.arrive.room`
- command rejection with `limbo.not_in_play` while a selected character is out of play
- exclusion from `look` contents and room-broadcast recipient planning; live delivery requires an active hub connection

Not yet implemented:

- session-expiry sweeps beyond explicit logout
- startup mass-limbo after server restart (snapshots may still load characters as in-world until the next disconnect)