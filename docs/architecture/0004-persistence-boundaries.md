# ADR 0004: Persistence boundaries and durability

Status: Accepted

## Context

BrokenRealm currently keeps one `GameState` in memory. That state combines permanent world objects, behavior source and compiled JavaScript, item definitions, and one temporary player. This is sufficient for the prototype, but persisting that record directly would couple durable world data to compiler caches, sessions, and the future account model.

The persistence boundary must be defined before accounts are added. Accounts identify people; characters own durable game progress; sessions authenticate a temporary connection. These have different lifecycles and should not become fields in one serialized kernel snapshot.

## Decision

Split state into four explicit categories.

### Durable world state

Persist:

- permanent objects, including immutable object IDs, behavior-class references, containment, tags, references, aliases, and typed properties
- anonymous behavior values as part of their containing typed value tree
- item and other world-level definitions that are not supplied by checked-in seed data
- a monotonically increasing world revision used for optimistic concurrency and operational diagnostics

Object references remain stored as stable object IDs. Derived contents collections are not persisted; they are reconstructed from `LocationId`. Anonymous values have no table or identity of their own.

### Durable authored behavior state

Persist each behavior module's:

- stable module ID
- dependency IDs
- TypeScript source
- source revision
- activation revision and activation timestamp

TypeScript source is authoritative. Compiled JavaScript, extracted class metadata, dependency closures, and Monaco models are rebuildable artifacts and are not authoritative persistent state. A storage adapter may cache compiled output keyed by the compiler contract version and the exact source/dependency revisions, but startup must remain able to rebuild it.

Saving behavior is a two-phase operation: compile and validate the complete affected graph without a write transaction, then atomically compare expected revisions and activate all changed module revisions. A failed compile, stale revision, or failed write leaves the previously active graph unchanged.

### Durable account and character state

When accounts are introduced, persist accounts and player characters separately. Account records own identity and authorization data. Character records own world position, inventory, preferences, and other game progress. Commands act as a character, never as a browser session or account record directly.

The current singleton `PlayerState` is prototype state. It must become a character-scoped record before authentication is connected to command execution.

### Ephemeral state

Do not include these in world snapshots:

- authenticated sessions, anti-forgery state, and connection presence
- in-flight commands and script tasks
- compiler processes, dependency closures, and compiled-script caches
- Monaco models, undo history, and unsaved editor drafts
- localized response text and other derived presentation data

Sessions may later use a dedicated shared store for multi-server deployment, but that store is operational session persistence, not the world database. Unsaved editor drafts require an explicit product feature before they become durable.

## Consistency and transaction boundaries

- One accepted player command is one atomic world mutation. All validated effects either commit against the expected world/character revision or none do.
- Behavior activation is a separate atomic transaction over the complete affected module graph.
- Behavior execution uses one immutable snapshot of the active behavior revisions and relevant world/character state. It may retry only before script execution; scripts are not assumed to be idempotent.
- Storage implementations expose compare-and-swap revisions rather than leaking database transactions into behavior code.
- Referential, containment, behavior-class, and typed-value invariants are validated both when loading persisted data and before committing mutations.
- A process-local lock remains acceptable for the in-memory adapter. A future multi-process adapter must enforce the same revision checks in its storage transaction.

## Migrations and recovery

- Durable records carry an explicit storage format version independent of the application version.
- Migrations are ordered, deterministic, forward-only transformations. They run before the kernel accepts commands or admin writes.
- Migrations preserve object and module IDs and fail loudly on invalid or ambiguous data; they do not silently discard fields or regenerate identities.
- Backups and restore operate on authoritative source and durable records, not compiled caches.
- Startup loads and validates durable data, recompiles active behavior graphs when no compatible cache exists, and only then reports readiness.
- Downgrades are performed by restoring a compatible backup; reverse migrations are not required.

## Storage abstraction

Introduce storage interfaces only when the first durable adapter is implemented. Keep them aligned with the transaction boundaries above rather than creating one repository per domain type. The initial in-memory implementation remains the reference behavior for tests.

The choice of PostgreSQL, another database, schema layout, session technology, password/authentication provider, and deployment topology is deliberately deferred. This ADR defines semantics those choices must preserve.

## Consequences

- A server restart may continue to reset state until a durable adapter is explicitly selected and implemented.
- Behavior source survives independently of compiler implementation changes; compiled artifacts can always be invalidated.
- Accounts can be added without making sessions the owner of game progress.
- Atomic effect application becomes a storage contract, not merely an in-process implementation detail.
- Admin saves need expected source revisions so concurrent editors cannot silently overwrite each other.

## Alternatives considered

### Serialize the current `GameState`

This is quick but persists derived compiled code, embeds a singleton player, provides no concurrency boundary, and makes schema evolution depend on an internal F# record layout.

### Event sourcing every mutation

An event log could support audit and replay, but it adds versioned event contracts, replay tooling, snapshots, and operational complexity before the product needs them. An append-only audit stream can be added later without making it the source of truth.

### Persist sessions with world state

Sessions expire and are security-sensitive operational data. Coupling them to world backups and migrations would give authentication state the wrong lifecycle and ownership.
