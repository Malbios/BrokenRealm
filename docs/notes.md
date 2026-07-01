# Future product notes

These are uncommitted product ideas, not current behavior or accepted architecture:

- Combat with many combatants could be presented as avatar-versus-avatar.
  Attack types and power could depend on each combatant's army size and composition.

## Mechanics backlog (agreed, not yet implemented)

Priority order for small vertical slices:

1. **Hunger stakes at 100** — hunger ticks to the cap and shows warnings at 50/80, but nothing happens at 100 yet (no limbo, death, or other consequence).
2. **Strongbox seed contents** — the village strongbox is locked with capacity 2 but starts empty; seed a reward inside to complete the craft-key → open loop.
3. **Farmer resume after `interruptedWork`** — talking while working sets `interruptedWork: true` in AI memory, but `activateRoot` never reads it to resume work.
4. **Hare recovery timer + `hareCap`** — forest spawns a hare immediately when none are present and ignores the seeded `hareCap`; add a cooldown property and cap-aware repopulation.
5. **Auth hardening (ADR 0005)** — login/register/logout exist and sessions persist across restarts; still open: password policy, rate limiting, account admin beyond prototype seed.
6. **Sidebar status polish** — the panel under the minimap shows name + location only; hunger or inventory summary would use the new layout without kernel changes.