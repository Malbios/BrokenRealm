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
- `src/BrokenRealm.Server/BehaviorSources.fs`: editable TypeScript behavior class module.
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

The kernel matches localized input against command patterns declared by the current object's TypeScript behavior class. The player starts at `forest` and can follow object references north to `village` and south back to `forest`. Behavior methods return neutral effects that the F# kernel validates and applies atomically.

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
- `GET /admin/behaviors/core-world`
- `PUT /admin/behaviors/core-world`

The browser admin panel loads the behavior-module catalog and uses Monaco, loaded from a pinned CDN version with a textarea fallback, to edit TypeScript class hierarchies in memory. Objects reference a module and class. Native `extends`, `override`, and `super` provide behavior inheritance. On save, the server type-checks and compiles the whole module, reads command metadata from its registered classes, verifies that every referenced class still exists, and atomically activates the new module. Failures leave the previous module active. There is no authentication yet.

## Script limits

Admin-authored verbs run with centralized limits: 4 MB tracked memory, 250 ms execution time, 64,000 source characters, 32 returned effects, 16 message effects, 16 arguments per message, and 1,024 characters per message argument value. Limit failures return stable diagnostics and reject the entire effect batch without mutating game state.

## Scope

There is no durable database, Docker setup, authentication, or SignalR. State and edited verb source are held in memory and reset when the server restarts.

## Object IDs

Object IDs are immutable, locale-neutral identifiers. Seeded objects may use stable semantic IDs such as `forest`; runtime-created objects use `obj_` followed by a UUIDv7 in compact hexadecimal form. IDs are restricted to 1-64 lowercase ASCII letters, digits, underscores, and hyphens, must begin with a letter, and are never changed when an object's display name changes. See `docs/architecture/0001-object-ids.md`.
