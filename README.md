# BrokenRealm

BrokenRealm is a browser-playable, ToastStunt-inspired text empire sandbox prototype. Its trusted F# kernel owns a revision-checked object database with file-backed JSON snapshots, localized command dispatch, sandboxed execution, effect validation, and atomic state changes. Admin-authored game behavior lives in TypeScript classes compiled to JavaScript and executed through Jint.

## Run

```powershell
dotnet run --project src/BrokenRealm.Server
```

Open the URL printed by ASP.NET Core, usually `http://localhost:5028`.

## Verify

```powershell
dotnet test BrokenRealm.slnx
```

```powershell
npx tsc --noEmit
```

Run the TypeScript check from `src/BrokenRealm.Client`.

## Structure

- `src/BrokenRealm.Server/Domain.fs`: typed value, object, behavior, state, message, effect, and transport types.
- `src/BrokenRealm.Server/Persistence.fs`: versioned authoritative snapshots and the revision-checked in-memory store.
- `src/BrokenRealm.Server/Kernel.fs`: generic command submission, behavior-method dispatch, and effect application.
- `src/BrokenRealm.Server/BehaviorSources.fs`: editable TypeScript behavior modules and checked-in compiled fragments.
- `src/BrokenRealm.Server/Scripting.fs`: Jint execution and script effect decoding.
- `src/BrokenRealm.Server/ScriptCompiler.fs`: TypeScript validation/compilation for admin-edited behavior modules.
- `src/BrokenRealm.Server/Scripting/game-api.d.ts`: typed API available to behavior modules.
- `src/BrokenRealm.Server/ObjectDatabase.fs`: in-memory starting object database.
- `src/BrokenRealm.Server/CommandMatching.fs`: localized behavior-command pattern matching.
- `src/BrokenRealm.Server/Localization.fs`: culture parsing and message/item localization.
- `src/BrokenRealm.Client`: browser TypeScript source.
- `tests/BrokenRealm.Tests`: focused F# kernel tests.

## Current Commands

English:

- `look`, `l`
- `gather wood`, `collect wood`
- `inventory`, `inv`
- `drop wood`, `drop 5 wood`
- `take wood`, `pick up 3 wood`
- `give wood to scout`, `give 2 wood to scout`
- `say hello`, `emote waves`, `: smiles`
- `go north`, `walk north`
- `examine log`, `x fallen log`
- `name trail green way`

Deutsch:

- `schau`, `umsehen`, `sieh dich um`
- `sammle holz`, `holz sammeln`
- `inventar`, `inv`
- `lege holz ab`, `lege 5 holz ab`
- `nimm holz`, `hebe 3 holz auf`
- `gib holz an scout`, `gib 2 holz an scout`
- `sag hallo`, `sage`, `emote winkt`, `* winkt`
- `gehe nach norden`, `geh nach süden`
- `untersuche baumstamm`
- `nenne pfad grüner weg`

The kernel matches localized input against command patterns declared by the current object's TypeScript behavior class. Characters have independent IDs, locations, and inventories; command execution names the acting character explicitly. Sessions select an account-owned character. Offline characters remain in limbo until `POST /game/session/enter` restores them at their last safe room; unauthenticated guest sessions do not enter play automatically. Behavior methods return neutral effects that the F# kernel validates and applies atomically.

Object properties use neutral typed values: null, strings, 64-bit integers, floating-point numbers, booleans, object references, lists, maps, and identity-free anonymous behavior values. They are exposed to behavior methods as ordinary JavaScript values. Nested object and behavior-class references are recursively validated before execution.

Permanent objects have an optional location. Contents are derived from those location references, and the kernel rejects missing locations, self-containment, and containment cycles. The forest contains a localized `fallen-log` object using `ThingBehavior`; `look` lists it and localized `examine` commands dispatch directly to its behavior class.

The forest also stores an anonymous `TrailTokenBehavior` value. Naming the trail invokes that value through its containing permanent object and atomically replaces the nested value. Anonymous behavior values have no object ID, location, aliases, tags, or direct command matching.

## API

`POST /game/command`

```json
{
  "text": "sammle holz",
  "culture": "de"
}
```

```json
{
  "lines": ["Du sammelst 2 Holz."]
}
```

Live room feed:

- `GET /game/hub` (SignalR WebSocket)
- server pushes `roomLine` events to connected characters in the same room for `.room` message keys (say, emote, drop, give, take, move)

World simulation:

- a configurable global tick (`WorldTickSeconds`, default 30) visits every room, even when no player is connected
- in-world objects tagged `creature` tick after their room in stable object-ID order; limbo players are excluded because they are outside the containment graph
- scripts receive a dedicated `TickContext` containing the target, room graph, storage summaries, connected players, tick index, and interval
- the seeded forest regrows wood and contains a wandering hare; players accumulate hunger while in play
- non-player creature decisions use persistent TypeScript goal stacks with timeouts, deterministic weighted selection, and actor-local memory; AI state and world actions commit atomically

Admin editor transport:

- `GET /admin/behaviors`
- `GET /admin/behaviors/{moduleId}`
- `PUT /admin/behaviors/{moduleId}`
- `POST /admin/behaviors/{moduleId}/validate`
- `GET /admin/scripting/game-api.d.ts`
- `POST /admin/snapshot/backup`
- `GET /admin/snapshots`
- `POST /admin/snapshot/restore`

Loading a behavior module returns its `sourceRevision`. Check and save requests must include that revision:

```json
{
  "source": "// complete TypeScript module source",
  "expectedSourceRevision": 0
}
```

Successful responses return the current `sourceRevision`. A stale revision returns HTTP 409 without compiling or activating the submitted source. Compiler failures return HTTP 400 with structured diagnostics; a missing module returns HTTP 404.

The class library is split into `core-behaviors`, `location-behaviors`, `forest-behaviors`, `village-behaviors`, `thing-behaviors`, and `anonymous-behaviors`. Modules declare dependencies; the kernel rejects missing dependencies and cycles and compiles dependency source in deterministic topological order. Native `extends`, `override`, and `super` work across module boundaries.

The browser admin panel loads the behavior-module catalog and uses Monaco, loaded from a pinned CDN version with a textarea fallback, to edit modules in memory. It loads the server's authoritative scripting declarations and dependency sources for IntelliSense, preserves one model and undo history per module, and maps structured diagnostics to the correct module and source location. Check validates the complete affected graph without activation. Save recompiles the edited module and all transitive dependents, validates every registered class and affected object, and activates the graph atomically. Both operations send the source revision loaded with the module; stale requests receive HTTP 409 and offer reload/overwrite actions while preserving unsaved editor contents. Unsaved-change guards cover module and tab navigation. Any failure leaves the previous graph active.

Player sessions support guest play, password login, registration, and character selection. The seeded prototype account is `prototype-account` with password `prototype`.

Capability contracts use normal TypeScript interfaces. `ForestBehavior`, for example, implements `Gatherable`, so removing its `gather` method produces a compiler diagnostic before activation.

## Script limits

Admin-authored behavior methods run with centralized limits: 4 MB tracked memory, 250 ms execution time, 64,000 source characters, 32 returned effects, 16 message effects, 16 arguments per message, and 1,024 characters per message argument value. Limit failures return stable diagnostics and reject the entire effect batch without mutating game state.

## Scope

There is no PostgreSQL or Docker setup. The process uses a revision-checked file-backed storage adapter that writes authoritative JSON snapshots to `data/game-snapshot.json` (override with `BROKENREALM_SNAPSHOT_PATH`). Snapshots retain world objects, player progress, accounts, and TypeScript source while excluding compiled behavior artifacts. Startup requires the current snapshot format, reconciles seed behavior lineage, and puts persisted players into limbo before accepting play. Admin snapshot backup and restore endpoints write timestamped copies under `data/backups/`. SignalR provides live room delivery and connection tracking. The durability, transaction, character, session, and limbo boundaries are defined in the architecture records; no database technology has been selected.

## Object IDs

Object IDs are immutable, locale-neutral identifiers. Seeded objects may use stable semantic IDs such as `forest`; runtime-created objects use `obj_` followed by a UUIDv7 in compact hexadecimal form. IDs are restricted to 1-64 lowercase ASCII letters, digits, underscores, and hyphens, must begin with a letter, and are never changed when an object's display name changes. See `docs/architecture/0001-object-ids.md`.
