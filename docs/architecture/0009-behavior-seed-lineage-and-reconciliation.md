# ADR 0009: Behavior seed lineage and graph reconciliation

Status: Accepted

## Context

BrokenRealm stores authoritative behavior module TypeScript in `game-snapshot.json`. Checked-in seed behavior also exists for first boot, tests, and development updates. Admins edit the same modules through the browser.

Before this decision, those two sources could diverge:

- Git/seed updates changed checked-in behavior without updating persisted modules.
- Cross-module references (for example craft recipes referencing `PlaceableBehavior`) could fail at command time instead of startup.
- Repairs were ad hoc (`repairMissing…`, `repairStale…`) rather than a single graph model.

ADR 0004 already requires authoritative TypeScript source, atomic activation over the affected dependency closure, and admin revision checks. This ADR extends that model to seed updates and startup reconciliation.

## Decision

### Seed authoring surface

Checked-in seed behavior lives in real TypeScript files:

```
src/BrokenRealm.Server/behaviors/seed/*.ts
```

`BehaviorSources.fs` is a loader and manifest helper only. It does not contain game logic strings.

### Lineage per persisted module

Each `BehaviorModuleSnapshot` records:

- `provenance`: `seedSynced` or `adminEdited`
- `syncedSeedHash`: SHA-256 of the seed source this module was last aligned with

Rules:

- Fresh or reconciled seed copies are `seedSynced`.
- Any successful admin `PUT` marks the module `adminEdited`.
- `adminEdited` modules are never silently overwritten by seed sync.

### Seed manifest

At runtime the server computes a manifest of current seed file hashes. Drift is detected when `syncedSeedHash` differs from the current seed hash.

### Graph reconciliation on load

Before commands are accepted, startup runs `BehaviorGraph.reconcileBehaviorModules`:

1. Insert missing referenced modules from seed.
2. Upgrade `seedSynced` modules whose stored source still matches `syncedSeedHash` when the seed hash changed.
3. Upgrade `seedSynced` modules that are missing classes required by the behavior graph.
4. Leave `adminEdited` modules untouched.
5. Honor `BROKENREALM_RESEED_BEHAVIORS=1` in development to force-upgrade all non-`adminEdited` modules.

After compile, `validateBehaviorGraphReferences` fails startup when the graph is still incoherent.

### Single activation pipeline

Admin save, merge-seed, startup hydration, and restore all compile and validate through the same dependency-closure activation rules defined in ADR 0004.

### Admin drift workflow

- `GET /admin/behaviors` and module detail responses expose provenance, seed drift, and graph warnings.
- `POST /admin/behaviors/{moduleId}/merge-seed` explicitly replaces a module with current seed source after confirmation in the admin UI.

## Consequences

- `git pull` plus server restart auto-upgrades untouched modules.
- Admin edits remain durable and are not clobbered by seed sync.
- Incoherent graphs fail at startup with actionable diagnostics.
- Snapshot format version 5 adds provenance metadata.
- Local development can still delete `game-snapshot.json` or set `BROKENREALM_RESEED_BEHAVIORS=1` for a hard reset.

## Alternatives considered

### Always overwrite persisted modules from seed on boot

Simple for development but destroys legitimate admin-authored worlds.

### Keep TypeScript embedded in F# strings

Avoids a file tree but makes diffs, review, and tooling worse while preserving the same desync problem.

### Regex-only stale repairs without lineage

Fixed individual symptoms but did not define ownership or protect admin edits.