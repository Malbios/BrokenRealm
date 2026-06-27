# ADR 0007: Carried items as contained permanent objects

Status: Accepted

## Context

ADR 0006 made player characters permanent `GameObject` records but kept inventory as a typed map property (`properties.inventory`). That works for the vertical slice but diverges from ToastStunt-style containment: carried items are not first-class world objects, do not participate in location-based containment, and force inventory mutations through a player-property shortcut rather than ordinary object lifecycle rules.

Player revision tracking also becomes ambiguous when inventory is a property while other durable progress uses containment. Promoting carried items to contained objects keeps inventory changes on the player revision boundary without treating them as world-object edits.

## Decision

### Carried item stacks

Each inventory entry becomes one permanent stack object:

- `LocationId` references the carrying player object ID
- tags include `carried` and the neutral item id (for example `wood`)
- properties include `itemId` (string) and `quantity` (integer)
- behavior uses the passive `thing-behaviors:ThingBehavior` module without player-facing commands
- stack IDs are runtime-generated (`obj_` + UUIDv7) except migration may synthesize deterministic ids `carried-{playerId}-{itemId}`

`addInventory` creates or increments a stack for the target player. Quantity zero removes the stack object from `Objects`.

### Script and player API compatibility

`VerbContext.actor.inventory` remains a `Record<string, number>` map. The scripting boundary derives it from contained stack objects at execution time. TypeScript behavior modules do not change their inventory command implementations.

### Persistence

Snapshot format version 4:

- carried stacks live in `world.objects` like any other permanent object
- player objects no longer store `properties.inventory`
- format version 3 snapshots migrate forward by synthesizing stack objects from legacy inventory maps and removing the property

Carried stacks are excluded from world-revision comparison. Inventory changes advance the owning player's revision only.

### Validation

The kernel validates that carried stacks:

- reference a known player as their location
- use a known `itemId` from `state.ItemIds`
- store a positive `quantity`
- do not use the legacy `properties.inventory` map on player objects

## Consequences

- Inventory becomes inspectable as ordinary containment data in snapshots and tests.
- Future mechanics (dropping, giving, combining, containers) can target stack objects directly.
- Snapshot size grows slightly because each item type per player is its own object.
- World revision no longer advances for inventory-only changes.

## Implementation status

Implemented:

- `CarriedItems` helpers for stack creation, inventory projection, and `addInventory` mutations
- snapshot format version 4 with v3 migration
- player revision tracking based on contained stacks
- startup snapshot flush for first-run seeds and shutdown snapshot flush

## References

- ADR 0001: object IDs
- ADR 0004: persistence boundaries
- ADR 0006: player objects