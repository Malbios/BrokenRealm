# BrokenRealm Codex Notes

These notes are repo memory for future Codex sessions. Keep them focused on project architecture and current implementation state. Do not add transient tool/debugging issues here.

## Product Goal

BrokenRealm is a browser-playable, MOO-inspired, mechanics-first text empire/sandbox game.

The core idea is a MOO-like object system with TypeScript instead of MOOcode:

- Players interact through localized text commands in the browser.
- Admins edit object verbs in-browser with Monaco, syntax highlighting, IntelliSense, and diagnostics.
- Most mutable game behavior should live in admin-authored TypeScript verbs attached to game objects.
- The trusted F# runtime should stay small and act as a kernel.

## Architecture Direction

Use the term **kernel** for the trusted F# runtime.

The kernel owns:

- object database/state read and write
- sessions later
- authentication and permissions later
- localized command dispatch
- TypeScript-to-JavaScript compilation for admin-authored verbs
- JavaScript execution sandboxing
- effect validation
- effect application
- localization of output
- diagnostics and script execution resource limits

Game logic should not become hardcoded F# command handlers. F# should dispatch to object verbs and apply validated neutral effects.

## Current Shape

Current repo layout:

- `src/BrokenRealm.Server`: F# ASP.NET Core server and kernel.
- `src/BrokenRealm.Client`: browser TypeScript UI source.
- `src/BrokenRealm.Server/wwwroot`: generated/static browser assets served by the server.
- `tests/BrokenRealm.Tests`: F# tests.

Current server modules:

- `Domain.fs`: culture, object, verb, state, message, effect, and DTO types.
- `Localization.fs`: culture parsing, message localization, item names and aliases.
- `ObjectDatabase.fs`: in-memory starting object database.
- `CommandMatching.fs`: localized command text to object verb match.
- `Scripting.fs`: Jint execution and script effect decoding.
- `ScriptCompiler.fs`: TypeScript validation/compilation for admin-edited verb source.
- `Kernel.fs`: command submission, verb dispatch, effect application, verb update.
- `Program.fs`: minimal HTTP/static host.

Current object model:

- Stable object ID: `forest`.
- The current player starts at `forest`.
- `forest` has tags including `forest` and `wood`.
- `forest` has string properties including its biome and resource item.
- `forest` references `village` to the north and has verbs: `look`, `gather`, `inventory`, `move`.
- `village` references `forest` to the south and has verbs: `look`, `inventory`, `move`.
- Object IDs are stable identifiers. Tags are semantic metadata and should not be confused with IDs.

Current endpoints:

- `POST /game/command`
  - Body: `{ "text": "...", "culture": "en" | "de" }`
  - Player command endpoint.
- `GET /admin/objects`
  - Lists objects and their editable verbs for the admin editor.
- `GET /admin/objects/{objectId}/verbs/{verbName}`
  - Loads editable verb source.
- `PUT /admin/objects/{objectId}/verbs/{verbName}`
  - TypeScript-checks/compiles editable verb source.
  - On failure, returns diagnostics and keeps the previous compiled verb active.

## Localization Rules

Player input and output must both be localized.

Pipeline:

1. localized player input
2. localized command parser/pattern matcher
3. neutral object/verb intent and neutral args
4. neutral game logic in TypeScript verb
5. neutral effects
6. F# kernel validates/applies effects
7. localized output

Supported cultures for now:

- `en`
- `de`

Supported player commands:

English:

- `look`
- `l`
- `gather wood`
- `collect wood`
- `inventory`
- `inv`
- `go north`
- `walk north`

German:

- `schau`
- `umsehen`
- `sieh dich um`
- `sammle holz`
- `holz sammeln`
- `inventar`
- `inv`
- `gehe nach norden`
- `geh nach süden`

Neutral item IDs:

- `wood`

Localized item display:

- English: `wood`
- German: `Holz`

## Scripting Rules

Admin-edited verb source is TypeScript.

Runtime execution is JavaScript through Jint.

The script API is declared in:

- `src/BrokenRealm.Server/Scripting/game-api.d.ts`

Verb scripts define:

```ts
function execute(context: VerbContext): VerbResult {
  return { effects: [] };
}
```

Scripts must return neutral effects. Scripts should not directly mutate state.

Known effects:

- `{ type: "addInventory", itemId: "wood", amount: number }`
- `{ type: "movePlayer", destinationId: string }`
- `{ type: "message", key: string, args?: Record<string, unknown> }`

The kernel validates effects before applying them.

## Current Vertical Slice

The browser UI has:

- player tab with output log, command input, and language selector
- admin tab with object and verb selectors plus Monaco, with a textarea fallback when the CDN is unavailable
- structured compile diagnostics displayed as Monaco markers and individual editor messages

Admin can change the gather amount, save the verb, and later player `gather wood` / `sammle holz` commands use the changed logic.

## Constraints

Do not add these yet unless explicitly requested:

- PostgreSQL
- Docker
- authentication
- SignalR
- procedural world generation
- combat
- markets
- crafting trees
- admin tools beyond the current verb editor

Prefer:

- small vertical slices
- F# for server/kernel/domain/parsing/localization/tests
- TypeScript for browser UI and admin-authored game logic
- in-memory state for now
- generic object/verb mechanisms over command-specific endpoints

Avoid:

- hardcoded F# game logic for individual commands
- command-specific REST endpoints for player verbs
- large empty architecture
- mixing source responsibilities without a clear boundary

## Verification Commands

From repo root:

```powershell
dotnet build BrokenRealm.slnx
dotnet test BrokenRealm.slnx
dotnet run --project src/BrokenRealm.Server
```

From `src/BrokenRealm.Client`:

```powershell
npx tsc
npx tsc --noEmit
```

The browser TypeScript source lives in `src/BrokenRealm.Client`. Do not run client npm commands from the old `scripts` folder; that folder was removed.

## Near-Term Next Steps

Good next slices:

- decide durable object ID strategy before persistence
