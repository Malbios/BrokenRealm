# Future product notes

These are uncommitted product ideas, not current behavior or accepted architecture:

- Combat with many combatants could be presented as avatar-versus-avatar.
  Attack types and power could depend on each combatant's army size and composition.

## Mechanics backlog (agreed, not yet implemented)

Priority order for small vertical slices:

1. **Hunger stakes at 100** — hunger ticks to the cap and shows warnings at 50/80, but nothing happens at 100 yet (no limbo, death, or other consequence).
2. **Strongbox seed contents** — the village strongbox is locked with capacity 2 but starts empty; seed a reward inside to complete the craft-key → open loop.
3. ~~**Farmer resume after `interruptedWork`**~~ — implemented.
4. ~~**Hare recovery timer + `hareCap`**~~ — implemented (`hareRecoveryTicks`, `hareRecoveryRemaining`).
5. **Auth hardening (ADR 0005)** — login/register/logout exist and sessions persist across restarts; still open: password policy, rate limiting, account admin beyond prototype seed.
6. ~~**Sidebar status polish**~~ — implemented (hunger + inventory in session response and sidebar).