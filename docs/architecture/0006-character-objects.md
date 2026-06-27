# ADR 0006: Character objects in the object database

Status: Accepted

## Context

BrokenRealm is a ToastStunt-inspired programmable object database. In that model, players are first-class objects: they have a location, contents or carried state, properties, and verbs. Rooms, things, and players share one database and one command-dispatch story.

The current runtime diverges from that model. Permanent `GameObject` records hold behavior-class references, tags, properties, containment, and aliases. `CharacterState` is a parallel map with only `Id`, `AccountId`, `LocationId`, and `Inventory`. Command matching scans the acting character's current location and visible contents, but never the actor. Player-facing commands such as `inventory` are inherited through the location's behavior-class chain (`LocationBehavior` extends `GameBehavior`), and room verbs such as `look` and `move` emit actor-specific effects (`movePlayer`, `addInventory`) that the F# kernel applies against `CharacterState`.

ADR 0004 and ADR 0005 correctly separate durable world data, behavior source, account identity, and ephemeral sessions. They do not require characters to remain a non-object record forever. The split was a vertical-slice shortcut, not the target MOO-like shape.

This ADR unifies playable characters with the object database while preserving the account and session boundaries from ADR 0005.

## Decision

### Playable characters are permanent objects

A playable character is a permanent `GameObject` in `GameState.Objects` with:

- a stable character object ID (seeded characters keep IDs such as `prototype-player`; runtime-created characters use the `obj_` contract from ADR 0001)
- tag `player` (and optionally additional semantic tags)
- `LocationId` pointing at the room or container that currently holds the character
- a behavior module and class reference, initially `player-behaviors:PlayerBehavior`
- typed properties for character-local state, including inventory and owning account
- localized name and aliases like any other permanent object

Characters are located *in* the world graph. A character standing in `forest` has `LocationId = Some "forest"`. The kernel continues to reject missing locations, self-containment, and containment cycles using the same rules as other permanent objects.

### Accounts and sessions stay outside the object database

ADR 0005 remains in force:

- **Accounts** identify people and authorization boundaries. They are not world objects.
- **Sessions** bind a browser connection to an account and selected character ID. They are ephemeral and not snapshotted.
- Ownership is expressed on the character object, not on the account record. Each player object stores `accountId` as a typed property (or equivalent validated field) that must match the session account when the character acts.

Command dispatch still receives an explicit acting character ID from the session layer. The kernel resolves that ID to a player object in `Objects`.

### Player behavior module

Introduce `player-behaviors` with `PlayerBehavior` extending `GameBehavior`.

`PlayerBehavior` owns actor-local commands and methods that should not depend on standing inside a particular room class:

- `inventory` / `inv` (moved from `GameBehavior`)
- future actor verbs such as `say`, `emote`, `wear`, or `drop`

Room behavior modules (`location-behaviors`, `forest-behaviors`, and so on) keep environment verbs:

- `look`, `move`, `gather`, room-specific atmosphere, and contained-object interaction

`GameBehavior` becomes the shared root for inventory-free utility behavior only if still needed by non-player objects. It no longer carries `inventory` once `PlayerBehavior` exists.

### Command matching order

`CommandMatching.tryMatchForCharacter` scans candidates in this order:

1. **Actor** — the selected player object
2. **Location** — the object referenced by the actor's `LocationId`
3. **Visible contents** — permanent objects whose `LocationId` equals the actor's current location

The first object whose registered localized pattern matches wins. This mirrors the MOO habit of checking the player, then the environment, then nearby items, while keeping BrokenRealm's existing pattern-matcher and behavior-class metadata.

Object-targeted commands (`examine log`) continue to match contained objects. Location commands (`look`, `go north`) continue to match the room. Actor commands (`inventory`) match the player object directly instead of accidentally depending on the room's class hierarchy.

### Script context

`VerbContext.actor` becomes a full neutral object summary aligned with `VerbContext.this`, not only `{ inventory }`:

- object ID
- name and description keys
- tags
- properties, including inventory and `accountId`
- references and directly contained permanent-object summaries as needed by scripting

`VerbContext.this` remains the object whose behavior method was invoked (room, thing, or player).

Neutral effects remain the mutation boundary. Player-specific effect names are generalized where needed:

| Current effect | Target shape |
| --- | --- |
| `movePlayer` | `moveObject` with `objectId` defaulting to the acting character when omitted, or an explicit `objectId` for scripted relocation of a known player |
| `addInventory` | `addInventory` with explicit `objectId` defaulting to the acting character, or a follow-up `replaceValue` on `properties.inventory` if inventory stays a map property |

The kernel validates that effect targets reference existing player objects, valid locations, and allowed item IDs before applying state changes.

### Inventory representation

Phase 1 of this ADR keeps inventory as a typed map property on the player object (for example `properties.inventory: { wood: 2 }`). This preserves today's mechanics with minimal serialization churn.

A later ADR may promote carried items to contained permanent objects or anonymous values. That is not required to make characters first-class objects.

### Persistence and revisions

Snapshot format version 3:

- stores player characters inside `world.objects`
- removes the top-level `characters` section used by format version 2
- keeps the top-level `accounts` section from ADR 0005

Per-character optimistic concurrency moves from a separate `characters` revision map to one of:

- a property revision on the player object, or
- a dedicated `playerRevisions` map keyed by character object ID

The storage adapter must still support compare-and-swap for individual player progress independent of unrelated world-object edits when possible.

### Admin and authoring

Player behavior is authored in TypeScript like any other behavior module. The admin editor lists `player-behaviors` alongside `forest-behaviors` and `thing-behaviors`.

Changing `PlayerBehavior` affects every object referencing that class, exactly like location or thing classes. Object-specific player customization uses subclasses or assigned class overrides, not a parallel character record type.

## Consequences

- BrokenRealm's runtime model matches the ToastStunt expectation that players, rooms, and items share one programmable object database.
- Actor verbs and room verbs have clear ownership boundaries in behavior modules.
- `CommandMatching` and effect application become symmetric: the acting entity is always an object ID in `Objects`.
- ADR 0005 account and session semantics stay valid; only the character storage shape changes.
- Snapshot migrations must rewrite existing format version 2 character records into player objects and re-home inventory and location fields.
- Tests, seed data, session summaries, and Monaco declarations must be updated together.

## Alternatives considered

### Keep `CharacterState` forever

This preserves the current implementation but permanently maintains two containment models, actor-specific effects, and room-borne `inventory` commands. It fights the stated MOO-like product goal.

### Player objects without behavior classes

Characters could be passive data objects manipulated only by room verbs and kernel effects. That recreates hardcoded player mechanics in F# or scatters actor logic across every location class. It violates ADR 0002.

### Accounts as world objects

MOO does not have account records. BrokenRealm still needs real authentication boundaries. Accounts remain outside the object database.

### Inventory only as contained child objects

More MOO-faithful long term, but it multiplies object lifecycle, command matching, and serialization work before the simpler map-property move proves the unified model.

## Migration plan

Work proceeds in small vertical slices. Each step keeps `dotnet test` green and preserves playable commands throughout.

### Phase 1 — Domain and conventions (no player-facing change)

- Add ADR 0006 and document the `player` tag and `player-behaviors` module in repo notes.
- Extend `GameObject` validation to recognize player objects and required properties (`accountId`, `inventory`).
- Add helper APIs: `tryGetPlayer`, `playersByAccount`, `actingPlayerObject state characterId`.
- Keep `CharacterState` and the format version 2 `characters` snapshot section temporarily; hydrate both representations in memory or treat `CharacterState` as a projection of player objects behind a single module boundary.

**Exit criteria:** kernel helpers can read a player object as the authoritative source for location and inventory.

### Phase 2 — Seed player objects alongside legacy characters

- Add `player-behaviors:PlayerBehavior` with `inventory` moved out of `GameBehavior`.
- Seed `prototype-player` and `prototype-scout` as permanent objects in `ObjectDatabase.initialState`.
- Mirror location and inventory into legacy `CharacterState` during a transition period so existing command and persistence paths keep working.

**Exit criteria:** player objects exist in seed data and pass validation; behavior graph compiles with the new module.

### Phase 3 — Command matching and script context

- Update `CommandMatching.tryMatchForCharacter` to scan actor → location → contents.
- Enrich `VerbContext.actor` in `game-api.d.ts` and `Scripting.fs`.
- Route `inventory` through the player object.
- Generalize `movePlayer` / `addInventory` effect decoding to target player object IDs.

**Exit criteria:** `inventory`, `look`, `move`, and `gather` behave as today when issued from seeded characters; new tests cover actor-first matching.

### Phase 4 — Persistence migration to format version 3

- Add `SnapshotMigrations.migrateV2` that:
  - creates a player `GameObject` for each format version 2 character record
  - copies `LocationId`, `Inventory`, and `AccountId` onto that object
  - assigns `player-behaviors:PlayerBehavior` when no explicit class is stored
  - removes the top-level `characters` section
- Update `FileGameStore` commit paths and revision checks to use player-object revisions.
- Delete the in-memory `CharacterState` map from `GameState` once all readers use player objects.

**Exit criteria:** loading a v2 snapshot migrates to v3; fresh seeds write v3 only; per-character CAS still works.

### Phase 5 — Cleanup and follow-ups

- Remove `CharacterState`, `movePlayer`, and actor-only inventory shortcuts from domain and kernel code.
- Update session summaries to read location from player objects.
- Add tests for multi-character accounts, foreign-character rejection, and player-object containment validation.
- Open a follow-up ADR if carried items should become contained objects or anonymous values.

## Implementation status

Implemented:

- `player-behaviors:PlayerBehavior` with actor-local `inventory` command
- seeded `prototype-player` and `prototype-scout` as permanent player objects in `Objects`
- actor-first command matching (actor → location → contents)
- enriched `VerbContext.actor` in `game-api.d.ts` and script execution
- snapshot format version 3 with `playerRevisions`; v1/v2 snapshots migrate forward
- removal of parallel `CharacterState` / `Characters` runtime map

Not yet implemented:

- generalized `moveObject` / explicit object-targeted inventory effects (legacy `movePlayer` / `addInventory` names remain)
- carried items as contained permanent objects
- localized display names for player objects in room contents

Depends on:

- ADR 0002 TypeScript behavior classes
- ADR 0004 persistence boundaries
- ADR 0005 account, character, and session boundaries (session and account rules unchanged)

## References

- ADR 0001: stable object IDs, including seeded semantic IDs for prototype characters
- ADR 0002: behavior classes and neutral effects
- ADR 0004: separate durable world, behavior, account, and character persistence categories
- ADR 0005: sessions select character IDs; accounts own characters through `AccountId`