# ADR 0012: Active entity goal stacks

Status: Accepted

## Context

BrokenRealm needs autonomous non-player entities whose behavior remains scriptable, inspectable, inexpensive, and durable across snapshots. The world tick and `TickContext` already execute TypeScript behavior for every in-world creature. A planner or kernel-owned behavior tree would add a second game-logic system and weaken the TypeScript behavior boundary.

The design is inspired by the low-tech AI described for FromSoftware games: a current stack of parameterized goals, imperative action selection, weighted choices, actor-local memory, and timeouts.

## Decision

- AI state is ordinary typed object data under `properties.ai`: root goal, goal stack, memory, deterministic RNG state, and the next goal ID.
- `active-entity-behaviors` provides `ActiveEntityBehavior` and reusable goal-stack helpers. Concrete TypeScript behavior classes choose actions and implement goals.
- One goal at most is updated for an entity during a world-tick pulse.
- Goal updates return neutral effects. The updated AI state and world action are applied as one validated atomic effect batch.
- Goal results are `continue`, `success`, or `failure`. Success pops the current goal. Failure pops it and unwinds pending sibling goals to its parent; a root failure clears the stack.
- Every goal has a tick deadline. Expired goals fail so malformed content cannot leave an entity permanently stuck. Because the process-local tick index resets on restart, persisted deadlines are rebased from the actor's last updated tick while preserving their remaining lifetime.
- Weighted choice uses a persisted 32-bit linear-congruential generator rather than `Math.random()`. Identical state and tick input therefore produce identical decisions.
- `Kernel.tickWorld` snapshots scheduled creature IDs at the beginning of each pulse. Each entity ticks at most once even if it moves to a room that is processed later; newly created entities begin on the following pulse.
- The kernel does not interpret goal kinds, weights, memory, or personality.

## Initial slice

`CreatureBehavior` derives from `ActiveEntityBehavior`. The seeded forest hare chooses among waiting, grazing, and wandering. Its first deterministic sequence waits and then wanders, proving persistent goals, deadlines, weighted selection, actor-local memory, movement, and offline autonomous execution.

Humanoid creatures retain their current no-op tick until a routine-oriented slice is designed.

## Interrupt delivery

- Scripts emit neutral `deliverInterrupt` effects with a target object id, interrupt kind, optional string args, and optional source id.
- The kernel appends interrupts to `properties.ai.pendingInterrupts` with a bounded queue, then immediately flushes them through the target behavior's `handleInterrupts` method.
- Autonomous ticks also pass queued interrupts through `TickContext.interrupts` before goal updates.
- `ActiveEntityBehavior` bubbles interrupts from the stack top through `handleGoalInterrupt`, then `handleInterrupt`. A consumed interrupt may clear the stack, push a replacement sequence, emit neutral effects, and skip the normal goal update for that pulse.
- Interrupt kinds and responses remain TypeScript behavior logic. The kernel does not interpret them.

## Goal composition

- Shared goal helpers (`createWaitGoal`, `createWanderGoal`, `createFleeGoal`, `pushActiveSequence`, `updateStandardGoal`) live in `active-entity-behaviors`.
- `activateRoot` may return multi-step sequences executed in order. The seeded hare graze/wander routines and farmer work/rest routines use this pattern.

## Deferred work

- Admin AI-state visualization and controlled single-step execution are separate tooling work.
- Combat is not introduced by this decision.

## Consequences

- Behavior authors can compose routines imperatively from small goals without a planner or behavior-tree editor.
- AI state is visible in snapshots and survives restarts without a parallel persistence format.
- Deterministic decisions make tests and future replay diagnostics practical.
- Schema conventions for `properties.ai` must remain backward-compatible or be migrated explicitly when active entities already exist in snapshots.

## References

- ADR 0002: TypeScript behavior classes
- ADR 0004: persistence boundaries
- ADR 0011: autonomous world tick and creature objects
- [The Low-Tech AI Of Elden Ring](https://nega.tv/posts/low-tech-ai-of-elden-ring.html)
