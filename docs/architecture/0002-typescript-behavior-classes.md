# ADR 0002: TypeScript behavior classes

Status: Accepted

## Context

BrokenRealm began with standalone TypeScript verb functions attached directly to database objects. That proved localized dispatch, sandboxed execution, neutral effects, and live admin editing, but it leaves inheritance and reusable behavior to a custom kernel mechanism.

ToastStunt is the primary conceptual reference for a programmable object database, but BrokenRealm is not implementing MOOcode compatibility. Its game authors work in TypeScript and should receive TypeScript's normal language semantics and tooling.

## Decision

BrokenRealm will be a TypeScript class-based game runtime backed by an F# object database.

- Database objects contain identity, mutable state, tags, properties, references, and a behavior class ID.
- Behavior classes contain localized command definitions and executable methods.
- Behavior classes use native TypeScript single inheritance through `extends`, method overriding, and `super`.
- TypeScript interfaces represent capability contracts. Interfaces do not supply behavior.
- Cross-cutting reusable behavior uses explicit composition or conventional TypeScript mixins when justified.
- The trusted F# kernel owns class compilation, dependency selection, sandboxing, method dispatch, resource limits, effect validation, and atomic effect application.
- Behavior methods receive controlled context data and return neutral effects. They do not mutate kernel state directly.
- Admin tooling edits behavior class modules, not isolated functions attached to individual objects.

## Consequences

- Parent calls use ordinary `super.method()`; BrokenRealm will not invent a MOO-style `pass()` API.
- JavaScript and TypeScript do not provide native multiple class inheritance. The runtime will not emulate it in the kernel.
- Object-specific behavior is represented by assigning a specialized behavior class, potentially a small subclass, to that object.
- Compilation units must include the selected class and its imported or registered dependencies.
- Updating a base class can affect every object using a descendant class, so compilation and activation must validate the affected class graph before replacing active code.
- The admin editor must expose class ownership and dependency impact clearly.

## Alternatives considered

### Kernel-level ordered multiple inheritance

This follows ToastStunt more directly but requires custom method resolution, conflict rules, inherited-property semantics, and a `pass()` equivalent. Those semantics duplicate facilities game authors already expect from TypeScript and complicate diagnostics and editor support.

### Standalone object-attached verbs without inheritance

This was the prototype implementation before this ADR was carried out. It was simple but duplicated behavior between objects and did not provide a natural reuse or override mechanism.

### TypeScript classes with emulated multiple inheritance

Mixin factories can be useful locally, but making them the fundamental object model would obscure resolution and `super` behavior. Mixins remain an explicit authoring technique, not a kernel feature.

## Migration status

Completed. The runtime now executes behavior classes through Jint, resolves native `super` across a dependency-ordered module graph, dispatches commands to behavior methods, and edits and atomically activates behavior modules. The standalone object-attached verb representation has been removed.
