# ADR 0011: Autonomous world tick and creature objects

Status: Proposed

## Context

BrokenRealm aims for a **living world**: rooms evolve, resources shift, and creatures act whether or not a player is watching. Game rules belong in **TypeScript behavior classes**; the F# kernel dispatches ticks, validates neutral effects, and persists state (ADR 0002, ADR 0010).

Today:

- `Kernel.tickWorld` runs on a **30-second** timer in `Program.fs`.
- It ticks only **rooms occupied by connected, in-play characters**.
- Each tick calls `LocationBehavior.tick` with a real player as `actor` in `VerbContext`.
- Forest already increments `tickCount`; village syncs `comfort` / `stocked` from contents.

That shape made sense as an early vertical slice, but it conflicts with two product goals:

1. **The world should feel alive on its own** — empty forests still change; hares still move while nobody is online.
2. **30 seconds is sluggish** for creatures, regrowth, and ambience — players notice the pause.

ADR 0008 restricts **player** presence while disconnected: limbo player objects leave the containment graph so offline bodies do not appear in rooms or receive live broadcasts. That policy does **not** apply to creatures and does **not** require freezing the world when no session is connected.

Performance is a real constraint once every room and creature ticks frequently, but the project is singleplayer-first with a tiny seeded graph. Prefer a simple global model now; optimize when measurements justify it.

## Decision

### Terminology (read this first)

| Term | Meaning |
|------|---------|
| **Creature** | Any embodied agent in the world graph — animals, monsters, humanoids, **and players**. Tag `creature` on all of them |
| **Player** | A creature controlled through account/session — also tag `player`, `PlayerBehavior`, account binding |
| **NPC** | A creature without `player` — autonomous scripts only |
| **Room** | Settlement/location object with exits and contents |

**Players are creatures with more capabilities** (inventory commands, session binding, account ownership). They are not a separate simulation category. NPC humanoids and player avatars share the `creature` tag and the same world-tick pipeline.

TypeScript may use subclasses (`PlayerBehavior extends CreatureBehavior`, `HumanoidCreatureBehavior extends CreatureBehavior`) for shared verbs. The kernel only sees tags and behavior classes.

**Connection does not gate world ticks.** Rooms and in-world creatures tick while the server runs. `connectedPlayers` in `TickContext` is optional script context only.

**Limbo** (ADR 0008) removes a **player** from the containment graph (`LocationId = None`). Limbo players are not in any room and are skipped by creature ticks until they re-enter play. NPC creatures are always in-world while they exist.

### One autonomous world tick

All simulation in this ADR runs on a **single world tick** while the server process runs:

| What ticks | Gated on player connection? |
|------------|----------------------------|
| Rooms | **No** |
| Creatures (`creature` tag, `LocationId = Some _`) — NPCs **and players** | **No** |
| Optional animated placeables | **No** |

The kernel must not encode creature AI, regrowth rates, or settlement formulas. It only schedules ticks, builds context, executes scripts, and applies validated effects.

### Autonomous world tick scope

Each world-tick pulse visits:

1. **Every room object** — permanent objects with no `LocationId` that are not players or carried stacks (same notion as `PlayerObjects.isRoomObject`).
2. **Every in-world creature in those rooms** — permanent objects with tag `creature` and `LocationId = Some _` (includes players in play and NPCs).

Order within a pulse:

1. Room `tick` first (environment, metrics, spawn hooks).
2. Room contents `tick` in stable object-ID order (creatures, future animated placeables).

Rooms with zero players still tick. Player presence must not gate world evolution.

### Tick interval

- Replace the hard-coded interval with a **configurable** `WorldTickSeconds` (development default: **30**, matching common MOO heartbeat cadence).
- Store optional `worldTickCount` on `GameState` or derive game-time from room properties; behavior scripts own what one tick means (growth step, minute, hour).
- The kernel does not interpret game calendars — only fires pulses and increments an optional neutral counter if behaviors need a shared clock.

Faster defaults are intentional. Slow down via configuration if a deployment needs it.

### Tick context without a player actor

`VerbContext.actor` assumes a commanding player (inventory, `locationId`, movement). Ambient world ticks often have **no relevant actor**.

Introduce a dedicated **`TickContext`** for world simulation:

```ts
declare interface TickContext {
  tick: {
    index: number;        // monotonic world tick counter (optional)
    seconds: number;      // configured interval
  };
  this: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: VerbObjectSummary[];
    storedItems: Record<string, number>;
    containerStorage: Record<string, Record<string, number>>;
  };
  room: {
    id: string;
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: VerbObjectSummary[];
    floorItems: Record<string, number>;
    containerStorage: Record<string, Record<string, number>>;
    connectedPlayers: VerbObjectSummary[]; // in-play + connected only; may be empty
  };
}
```

- `LocationBehavior.tick(context: TickContext)` and `CreatureBehavior.tick(context: TickContext)` use **`this` / `room`**, not a fake player.
- `VerbContext` remains for player commands.
- `connectedPlayers` lists in-play, connected **player** objects in the room. It is optional context only (e.g. a creature script might choose to greet someone). **Creature and room ticks run when this list is empty.**

Existing `tick()` methods that take `VerbContext` are updated to `TickContext` during implementation. This is a behavior-module change, not F# game logic.

### Creature objects

Creatures are **permanent objects** in the object database:

- Animals (hare, wolf)
- Monsters (slime, undead)
- Humanoids (guard, farmer, traveler)
- **Players** (human-controlled; `player` + `creature` tags)

Shared shape:

- Stable or runtime `obj_` IDs per ADR 0001.
- `LocationId` points at a room (or future valid container) while the creature is in the world graph.
- Tags include **`creature`** on every embodied agent, plus `player` for account-bound avatars and role tags (`herbivore`, `predator`, `humanoid`, …).
- `properties` hold script-owned state (`hunger`, `mood`, `lastWanderTick`, `dialogueState`, …).
- `CreatureBehavior` (TypeScript) defines `tick`, command patterns (`examine`, `approach`, `talk`, …), and returns neutral effects (`moveObject`, `replaceValue`, `message`, `createObject`, `destroyObject`).

Humanoid creatures use the same tick pipeline as animals. Dialogue, combat, and faction logic live in TypeScript methods — not F# command handlers.

No kernel `if creature then …` branches. Spawning and despawning use existing `createObject` / `destroyObject` effects.

### Kernel responsibilities (generic only)

The F# changes are scheduling and plumbing:

- `tickWorld : GameState -> Result<GameState, string>` — no `isCharacterConnected` gate for room selection.
- Enumerate rooms and tickable contents; call `Scripting.executeBehaviorTick` with `TickContext`.
- Apply tick effects through the same `applyEffects` path as commands (atomic batches, limits, validation).
- Expose `WorldTickSeconds` from configuration.
- Persist world changes through the existing snapshot adapter.

Optional future kernel helpers (still generic): `countTaggedContents` exposed on `TickContext.room` is already assembled from object graph reads — no new F# settlement simulators.

### Performance posture

Do **not** solve performance upfront by tying the world to connected players.

If profiling shows cost:

1. **Round-robin staggering** — each pulse ticks a slice of rooms; full cycle completes every `WorldTickSeconds × sliceCount`. Creatures in ticked rooms run in the same slice.
2. **Activity hints** — scripts may no-op quickly when `room.connectedPlayers` is empty and properties indicate dormancy; logic stays in TypeScript.
3. **Existing script limits** — memory, timeout, and effect caps per tick invocation.

Measure on the seeded graph plus a synthetic “many rooms / many creatures” test before adding tiers or skipping.

### Persistence and offline worlds

World ticks mutate the authoritative snapshot while the server runs. If the process stops, simulation pauses — acceptable for development. Durability follows ADR 0004; no separate creature queue.

When no client is connected, the world still advances in memory and flushes on the existing revision schedule.

## Implementation phases

### Phase A — Tick pipeline (kernel + API)

- Add `TickContext` to `game-api.d.ts`.
- Refactor `tickWorld` to visit all rooms; remove connected-player gating.
- Migrate `LocationBehavior.tick` / `VillageBehavior.tick` / `ForestBehavior.tick` to `TickContext`.
- Set default `WorldTickSeconds = 30`; keep override in configuration.
- Tests: world ticks with zero connected clients; village metrics still update; forest `tickCount` advances.

### Phase B — First creature (TypeScript + seed)

- Add `CreatureBehavior` in a seed module (or extend `thing-behaviors`).
- Seed one `forest-hare` in `forest` with simple `tick`: wander via `moveObject`, bump a property.
- Localized `look` / `examine` commands on the creature.
- Tests: hare moves over multiple ticks without a player connected; examine works when a player is present.

### Phase C — Ecology hooks (TypeScript)

- Forest `tick` may spawn a hare when population property low and tag count below cap (`createObject`).
- Village `tick` reacts to `creature` tags in `room.contents` for ambience messages only.
- Document pattern for authors: environment in room `tick`, agency in creature `tick`.

## Relationship to ADR 0008

| Topic | ADR 0008 | ADR 0011 |
|-------|----------|----------|
| Offline **player** body | Limbo — not in any room | Unchanged; players only |
| **Creature** in a room | Not specified | Always simulated; limbo does not apply |
| Forest regrowth | Not specified | World tick |
| Creature / NPC activity | Not specified | World tick (all NPC types) |
| Room broadcasts | Live connected players only | Unchanged; tick `message` effects do not invent mailboxes |

Player-only vitals (hunger, fatigue) are implemented in `PlayerBehavior.tick` like any other creature script — not a separate kernel lane. Limbo players skip ticks because they leave the containment graph, not because a session disconnected.

## Open questions

1. **Shared game clock** — single `GameState.worldTickCount` vs per-room `tickCount` properties? Start with per-room/properties; add global counter if behaviors need alignment.
2. **`tickable` tag** — require explicit tag vs tick any content whose class defines `tick`? Prefer explicit `creature` / `tickable` tag to avoid accidental ticks on crates.
3. **Cross-room creature movement** — `moveObject` to another room ID is already valid for placeables; creatures reuse the same effect.

## Consequences

- Players returning after absence see a world that changed without them — core sandbox fantasy.
- Admin-authored TypeScript controls all evolution and creature rules.
- Kernel stays small: schedule, context, execute, validate, persist.
- Faster ticks increase CPU use linearly with room and creature count; staggering remains available without redesign.
- `VerbContext` / `TickContext` split makes it harder to accidentally read player inventory from ambient room ticks.

## References

- ADR 0002: TypeScript behavior classes
- ADR 0004: Persistence boundaries
- ADR 0006: Character objects
- ADR 0008: Offline character limbo
- ADR 0010: Object-first settlements