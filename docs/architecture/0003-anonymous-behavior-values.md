# ADR 0003: Anonymous behavior values

Status: Accepted

## Context

BrokenRealm needs lightweight, waif-like values for behavior-bearing data that does not warrant a permanent world object. Examples include a configurable token, effect descriptor, or embedded game component. Treating these as permanent objects would give them irrelevant identity, containment, aliases, and lifecycle concerns. Treating them as plain maps would prevent them from using the TypeScript behavior-class model.

## Decision

Add anonymous behavior values as a recursive `GameValue` variant.

An anonymous value contains:

- a behavior module ID and behavior class name
- typed string-keyed properties, whose values may include other game values

Anonymous values:

- have no object ID or independent identity
- have no location, contents, aliases, tags, or command matching
- are stored by embedding them in permanent state, initially in object properties
- are immutable values; changing one replaces the containing value
- live exactly as long as they are reachable from permanent state
- use the existing compiled TypeScript behavior graph, sandbox, limits, and neutral-effect boundary

The kernel validates nested permanent-object references and anonymous behavior-class references before invocation. A behavior invocation receives an anonymous-specific context rather than a fabricated permanent object.

## Consequences

- Equal anonymous values are indistinguishable; authors cannot rely on reference identity.
- No separate allocation table, ID generator, persistence table, tracing garbage collector, or containment rules are required.
- Recursive lists, maps, and anonymous values form one typed value tree and serialize with their containing permanent state.
- Anonymous values cannot directly receive player commands. Permanent object behavior must deliberately retrieve and invoke them.
- Future mutation effects must address a path rooted in permanent state and atomically replace the value at that path.
- Cyclic anonymous structures are impossible in the F# value representation.

## Alternatives considered

### Lightweight objects with generated IDs

This makes references and mutation straightforward, but recreates a second object heap with allocation, persistence, garbage collection, and dangling-reference semantics before those costs are justified.

### Plain maps without behavior

Plain maps remain appropriate for passive structured data, but they cannot express a reusable TypeScript behavior contract.

### Fabricated permanent-object contexts

Passing empty IDs, tags, containment, and aliases to anonymous behavior would make the API appear more uniform while weakening its type guarantees. A distinct context states the actual capabilities.
