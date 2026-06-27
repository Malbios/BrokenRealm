# BrokenRealm

BrokenRealm is a browser-playable, MOO-inspired text empire sandbox prototype. The current slice is intentionally small: one F# ASP.NET Core kernel, a static browser UI, an in-memory object database, localized verb dispatch, admin-edited TypeScript verbs compiled to JavaScript, server-side verb execution through Jint, and localized output.

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

- `src/BrokenRealm.Server/Domain.fs`: object, verb, state, message, and effect types.
- `src/BrokenRealm.Server/Kernel.fs`: generic command submission, behavior-method dispatch, and effect application.
- `src/BrokenRealm.Server/BehaviorSources.fs`: editable TypeScript behavior modules and checked-in compiled fragments.
- `src/BrokenRealm.Server/Scripting.fs`: Jint execution and script effect decoding.
- `src/BrokenRealm.Server/ScriptCompiler.fs`: TypeScript validation/compilation for admin-edited behavior modules.
- `src/BrokenRealm.Server/Scripting/game-api.d.ts`: typed API available to behavior modules.
- `src/BrokenRealm.Server/ObjectDatabase.fs`: in-memory starting object database.
- `src/BrokenRealm.Server/CommandMatching.fs`: localized verb pattern matching.
- `src/BrokenRealm.Server/Localization.fs`: culture parsing and message/item localization.
- `src/BrokenRealm.Client`: browser TypeScript source.
- `tests/BrokenRealm.Tests`: focused F# kernel tests.

## Current Commands

English:

- `look`
- `l`
- `gather wood`
- `collect wood`
- `inventory`
- `inv`
- `go north`
- `walk north`
- `examine log`
- `x fallen log`

Deutsch:

- `schau`
- `umsehen`
- `sieh dich um`
- `sammle holz`
- `holz sammeln`
- `inventar`
- `inv`
- `gehe nach norden`
- `geh nach süden`
- `untersuche baumstamm`

The kernel matches localized input against command patterns declared by the current object's TypeScript behavior class. The player starts at `forest` and can follow object references north to `village` and south back to `forest`. Behavior methods return neutral effects that the F# kernel validates and applies atomically.

Object properties use neutral typed values: null, strings, 64-bit integers, floating-point numbers, booleans, object references, lists, and maps. They are exposed to behavior methods as ordinary JavaScript values. Object references are recursively validated before execution.

Permanent objects have an optional location. Contents are derived from those location references, and the kernel rejects missing locations, self-containment, and containment cycles. The forest contains a localized `fallen-log` object using `ThingBehavior`; `look` lists it and localized `examine` commands dispatch directly to its behavior class.

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

Admin editor transport:

- `GET /admin/behaviors`
- `GET /admin/behaviors/{moduleId}`
- `PUT /admin/behaviors/{moduleId}`

The class library is split into `core-behaviors`, `location-behaviors`, `forest-behaviors`, `village-behaviors`, and `thing-behaviors`. Modules declare dependencies; the kernel rejects missing dependencies and cycles and compiles dependency source in deterministic topological order. Native `extends`, `override`, and `super` work across module boundaries.

The browser admin panel loads the behavior-module catalog and uses Monaco, loaded from a pinned CDN version with a textarea fallback, to edit a selected module in memory. It reports which dependent modules and objects will be affected. On save, the server recompiles the edited module and all transitive dependents, validates every registered class and affected object, and activates the complete graph atomically. Any failure leaves the previous graph active. There is no authentication yet.

Capability contracts use normal TypeScript interfaces. `ForestBehavior`, for example, implements `Gatherable`, so removing its `gather` method produces a compiler diagnostic before activation.

## Script limits

Admin-authored verbs run with centralized limits: 4 MB tracked memory, 250 ms execution time, 64,000 source characters, 32 returned effects, 16 message effects, 16 arguments per message, and 1,024 characters per message argument value. Limit failures return stable diagnostics and reject the entire effect batch without mutating game state.

## Scope

There is no durable database, Docker setup, authentication, or SignalR. State and edited verb source are held in memory and reset when the server restarts.

## Object IDs

Object IDs are immutable, locale-neutral identifiers. Seeded objects may use stable semantic IDs such as `forest`; runtime-created objects use `obj_` followed by a UUIDv7 in compact hexadecimal form. IDs are restricted to 1-64 lowercase ASCII letters, digits, underscores, and hyphens, must begin with a letter, and are never changed when an object's display name changes. See `docs/architecture/0001-object-ids.md`.
