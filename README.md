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
- `src/BrokenRealm.Server/Kernel.fs`: generic command submission, verb dispatch, and effect application.
- `src/BrokenRealm.Server/Scripting.fs`: Jint execution and script effect decoding.
- `src/BrokenRealm.Server/ScriptCompiler.fs`: TypeScript validation/compilation for admin-edited verb source.
- `src/BrokenRealm.Server/Scripting/game-api.d.ts`: typed script API available to verb source.
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

Deutsch:

- `schau`
- `umsehen`
- `sieh dich um`
- `sammle holz`
- `holz sammeln`
- `inventar`
- `inv`

The kernel matches localized input against verb patterns on the current object. The current location object is `forest`, and its editable `gather` verb returns neutral effects that the F# kernel validates and applies.

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

- `GET /admin/objects/forest/verbs/gather`
- `PUT /admin/objects/forest/verbs/gather`

The browser admin panel uses Monaco, loaded from a pinned CDN version with a textarea fallback, to edit the `gather` verb source in memory. On save, the server runs TypeScript checking/compilation first. If compilation fails, diagnostics are returned and the previously running verb stays active. There is no authentication yet.

## Scope

There is no durable database, Docker setup, authentication, or SignalR. State and edited verb source are held in memory and reset when the server restarts.
