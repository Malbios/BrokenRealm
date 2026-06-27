# BrokenRealm Codex Notes

These notes are repo memory for future Codex sessions. Keep them focused on project architecture and current implementation state. Do not add transient tool/debugging issues here.

## Product Goal

BrokenRealm is a browser-playable, ToastStunt-inspired, mechanics-first text empire/sandbox game.

The core idea is a TypeScript class-based game runtime backed by a MOO-like object database:

- Players interact through localized text commands in the browser.
- Admins edit behavior classes in-browser with Monaco, syntax highlighting, IntelliSense, and diagnostics.
- Most mutable game behavior should live in admin-authored TypeScript classes referenced by game objects.
- The trusted F# runtime should stay small and act as a kernel.

## Design Reference

Use ToastStunt, rather than original LambdaMOO alone, as the primary conceptual reference for the programmable object system. Preserve BrokenRealm's own technology choices: TypeScript behavior classes, a trusted F# kernel, browser-first interaction, neutral effects, and localized input/output.

Relevant ToastStunt concepts include:

- programmable database-backed objects
- reusable behavior and property conventions
- permanent objects and lightweight anonymous/waif-like values
- typed collection values
- object lifecycle and containment
- task limits, diagnostics, and runtime introspection
- behavior on primitive/value prototypes where useful

Do not pursue source compatibility with MOOcode or copy ToastStunt's C server architecture, networking stack, SQLite built-ins, or operating-system integrations. Adopt concepts only when they support BrokenRealm's product goal.

## Architecture Direction

Use the term **kernel** for the trusted F# runtime.

The kernel owns:

- object database/state read and write
- sessions later
- authentication and permissions later
- localized command dispatch
- TypeScript-to-JavaScript compilation for admin-authored behavior modules
- JavaScript execution sandboxing
- effect validation
- effect application
- localization of output
- diagnostics and script execution resource limits

Game logic should not become hardcoded F# command handlers. F# should dispatch to behavior methods and apply validated neutral effects.

## Planned Behavior Model Direction

BrokenRealm uses real TypeScript class semantics rather than recreating MOO inheritance inside the F# kernel. The decision is recorded in `docs/architecture/0002-typescript-behavior-classes.md`.

- Game objects store mutable state and reference a behavior class ID.
- TypeScript behavior classes contain command definitions and methods.
- Native `extends`, `override`, and `super` provide single inheritance.
- TypeScript interfaces describe capability contracts but do not provide runtime behavior.
- Shared behavior outside the class hierarchy uses explicit composition or mixins.
- The kernel compiles a behavior class together with its dependencies, instantiates it in the sandbox, invokes the selected method, validates its neutral effects, and applies them atomically.
- Admin editing targets behavior classes/modules rather than isolated object-attached functions.
- Object IDs, tags, properties, and references remain database concepts independent of the class hierarchy.

Do not add a parallel kernel-level multiple-inheritance or `pass()` mechanism. If a behavior needs its parent implementation, use native TypeScript `super`.

## Current Shape

Current repo layout:

- `src/BrokenRealm.Server`: F# ASP.NET Core server and kernel.
- `src/BrokenRealm.Client`: browser TypeScript UI source.
- `src/BrokenRealm.Server/wwwroot`: generated/static browser assets served by the server.
- `tests/BrokenRealm.Tests`: F# tests.

Current server modules:

- `Domain.fs`: culture, typed game value, object, behavior, state, message, effect, and DTO types.
- `Persistence.fs`: versioned authoritative snapshots and the revision-checked in-memory storage adapter.
- `Localization.fs`: culture parsing, message localization, item names and aliases.
- `ObjectDatabase.fs`: in-memory starting object database.
- `BehaviorSources.fs`: active TypeScript behavior modules, localized command metadata, and checked-in compiled fragments.
- `CommandMatching.fs`: localized command text to behavior-method match.
- `Scripting.fs`: Jint execution and script effect decoding.
- `ScriptCompiler.fs`: TypeScript validation/compilation for admin-edited behavior modules.
- `Kernel.fs`: command submission, behavior-method dispatch, effect application, behavior-module update.
- `Program.fs`: minimal HTTP/static host.

Current object model:

- Stable object ID: `forest`.
- The seeded `prototype-player` character starts at `forest`; the unauthenticated command endpoint selects it explicitly until sessions exist.
- `forest` has tags including `forest` and `wood`.
- `forest` has typed properties including strings, integers, booleans, lists, maps, floating-point values, and an object reference.
- `forest` references `village` to the north and uses `forest-behaviors:ForestBehavior`.
- `village` references `forest` to the south and uses `village-behaviors:VillageBehavior`.
- `fallen-log` is a permanent object located in `forest` and uses `thing-behaviors:ThingBehavior`.
- `forest.properties.trailToken` is an identity-free anonymous behavior value using `anonymous-behaviors:TrailTokenBehavior`.
- Anonymous behavior values embed recursive typed properties, live by reachability from permanent state, and have no ID, location, contents, aliases, tags, or direct command matching. The decision is recorded in `docs/architecture/0003-anonymous-behavior-values.md`.
- Permanent object contents are derived from each object's optional `LocationId`; no second mutable contents list is stored.
- Missing locations, self-containment, and containment cycles are rejected before command dispatch.
- Object IDs are stable identifiers. Tags are semantic metadata and should not be confused with IDs.
- Seeded objects may use reserved semantic IDs. Future runtime-created objects use `obj_` plus a UUIDv7. IDs are immutable and follow the contract in `docs/architecture/0001-object-ids.md`.
- Live command dispatch reads localized command metadata from compiled behavior classes and invokes class methods through Jint. `ForestBehavior.look()` uses native `super.look()`.
- The behavior graph contains `core-behaviors <- location-behaviors <- forest-behaviors|village-behaviors`, plus `core-behaviors <- thing-behaviors` and `core-behaviors <- anonymous-behaviors`.
- Module dependencies are compiled in deterministic topological order. Missing dependencies and cycles are rejected.
- Updating a module recompiles all transitive dependents and atomically activates the complete affected graph. Admin responses report affected modules and objects.

Current endpoints:

- `POST /game/command`
  - Body: `{ "text": "...", "culture": "en" | "de" }`
  - Player command endpoint.
- `GET /admin/behaviors`
  - Lists editable behavior modules and their registered classes.
- `GET /admin/behaviors/{moduleId}`
  - Loads an editable TypeScript behavior module with its current source revision.
- `PUT /admin/behaviors/{moduleId}`
  - Requires `{ source, expectedSourceRevision }`, TypeScript-checks/compiles the module, reads registered class command metadata, and verifies referenced classes.
  - Returns HTTP 409 before compilation when the loaded revision is stale.
  - On failure, returns diagnostics and keeps the previous compiled module active.
- `POST /admin/behaviors/{moduleId}/validate`
  - Requires `{ source, expectedSourceRevision }` and runs the same dependency-graph compilation and validation as save without activating or returning candidate state.
  - Returns HTTP 409 when validation is based on stale source.
- `GET /admin/scripting/game-api.d.ts`
  - Serves the authoritative TypeScript scripting declarations used by Monaco.

## Localization Rules

Player input and output must both be localized.

Pipeline:

1. localized player input
2. localized command parser/pattern matcher
3. neutral object/behavior-method intent and neutral args
4. neutral game logic in a TypeScript behavior method
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
- `examine log`
- `x fallen log`
- `name trail green way`

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
- `untersuche baumstamm`
- `nenne pfad grüner weg`

Neutral item IDs:

- `wood`

Localized item display:

- English: `wood`
- German: `Holz`

## Scripting Rules

Admin-edited behavior modules are TypeScript.

Runtime execution is JavaScript through Jint.

The script API is declared in:

- `src/BrokenRealm.Server/Scripting/game-api.d.ts`

Behavior classes define methods returning `VerbResult` and register localized command patterns in static `commands` metadata.

For example:

```ts
class ForestBehavior extends LocationBehavior {
  override look(context: VerbContext): VerbResult {
    const parent = super.look(context);
    return { effects: [...parent.effects] };
  }
}
```

Scripts must return neutral effects. Scripts should not directly mutate state.

Object properties use the `GameValue` union:

- null
- string
- signed 64-bit integer
- floating-point number
- boolean
- validated object reference
- recursive list
- recursive string-keyed map
- anonymous behavior value with a behavior-class reference and recursive properties

The scripting boundary converts these to ordinary JavaScript values. Object references nested in lists, maps, or anonymous values are recursively validated against the object database before a behavior method executes. Anonymous behavior module and class references are also validated. Anonymous methods receive `AnonymousBehaviorContext`, which exposes only properties, arguments, and actor inventory rather than fabricating a permanent-object context.

`GameState` stores characters by stable character ID. Command dispatch, matching, effects, and anonymous behavior invocation receive an explicit acting character ID; character location and inventory are not singleton world fields.

`VerbContext.this.contents` contains neutral summaries of directly contained permanent objects. Location `look` methods use those IDs to emit neutral content-list messages; the F# response formatter resolves localized object names. Localized object aliases are used only for input matching and resolve to stable object IDs before behavior dispatch.

Capability contracts are TypeScript interfaces declared in `game-api.d.ts`. `ForestBehavior implements Gatherable`; interfaces provide compile-time requirements but no runtime behavior.

Known effects:

- `{ type: "addInventory", itemId: "wood", amount: number }`
- `{ type: "movePlayer", destinationId: string }`
- `{ type: "replaceValue", path: (string | number)[], value: GameValue }`
- `{ type: "invokeAnonymous", path: (string | number)[], methodName: string, args?: Record<string, string> }`
- `{ type: "message", key: string, args?: Record<string, unknown> }`

The kernel validates effects before applying them. `replaceValue` and `invokeAnonymous` paths are rooted at the permanent object whose behavior is executing; scripts cannot select an owner object ID. Paths traverse object properties, maps, lists, and anonymous-value properties. Stored anonymous behavior receives its kernel-controlled `storagePath`. Replacements rebuild the value tree and the complete effect batch remains atomic. Nested anonymous dispatch is limited to 8 levels and 16 invocations per root effect batch.

Script execution limits are centralized in `Scripting.defaultLimits`:

- 4 MB tracked memory
- 250 ms execution timeout
- 64,000 source characters
- 32 effects per result
- 16 message effects per result
- 16 arguments per message
- 1,024 characters per message argument value

Limit failures use stable sanitized diagnostics. Effects are decoded and validated as a complete batch before the kernel applies any state changes.

## Current Vertical Slice

The browser UI has:

- player tab with output log, command input, and language selector
- admin tab with behavior-module selection plus Monaco, with a textarea fallback when the CDN is unavailable
- structured compile diagnostics displayed as Monaco markers and individual editor messages
- Monaco declarations loaded from the server's real `game-api.d.ts` contract rather than a duplicated browser string
- dependency-aware TypeScript IntelliSense using the selected module's transitive behavior sources
- one Monaco model per behavior module, preserving undo history and unsaved edits while switching modules
- structured class, dependency, affected-module, and affected-object metadata visible before save
- compiler diagnostics mapped to behavior module IDs and module-local locations; Monaco marks the corresponding cached model and diagnostic entries open that module
- a Check action that compiles the full affected graph without activation
- optimistic concurrency using loaded source revisions; stale checks and saves preserve editor contents and require a reload rather than overwriting newer source
- unsaved-change guards for module switches, leaving the admin tab, and page unload; per-module Monaco models retain edits during in-page navigation

Admin can change behavior methods or command metadata, save the module, and later player commands use the atomically activated class hierarchy.

## Constraints

Do not add these yet unless explicitly requested:

- PostgreSQL
- Docker
- SignalR
- procedural world generation
- combat
- markets
- crafting trees
- admin tools beyond the current behavior editor

Prefer:

- small vertical slices
- F# for server/kernel/domain/parsing/localization/tests
- TypeScript for browser UI and admin-authored game logic
- file-backed JSON snapshots for development durability
- generic object/behavior mechanisms over command-specific endpoints

Avoid:

- hardcoded F# game logic for individual commands
- command-specific REST endpoints for player behavior methods
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

1. Add carried-items ADR and promote inventory from player property maps to contained objects where appropriate.
2. Add player presence verbs (`say`, `emote`) and richer multi-character room visibility.
3. Select and implement a durable database adapter only after the file-backed snapshot contract has proven sufficient in development.
