# ADR 0001: Durable object identifiers

Status: Accepted

## Decision

Object IDs are immutable, locale-neutral strings with this syntax:

```text
^[a-z][a-z0-9_-]{0,63}$
```

Seed data may reserve short semantic IDs such as `forest` and `village`. Objects created at runtime use `obj_<uuidv7>`, where the UUID is lowercase compact hexadecimal without separators.

Display names, localized names, tags, and aliases are separate mutable metadata. Renaming an object never changes its ID. Object references and future persistence relations store the ID, not a display name or tag.

## Rationale

- UUIDv7 provides decentralized generation and useful chronological locality for future database indexes.
- The `obj_` prefix keeps generated IDs recognizable in logs and admin tools.
- Stable semantic IDs keep authored seed content readable.
- A restricted ASCII format is safe in URLs, JSON, logs, and database keys.
- Immutability prevents broken references when names or localization change.

## Persistence implications

- Store IDs as text with a maximum length of 64 and a unique or primary-key constraint.
- Validate IDs at object-creation and import boundaries.
- Reject duplicate IDs; never silently regenerate or rewrite imported references.
- Do not expose sequential database keys as game object identity.
