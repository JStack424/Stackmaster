# ADR 0002: Pure planning before Valheim mutation

Status: Accepted on September 11, 2026

## Decision

Stackmaster divides inventory work into three layers:

1. **Snapshots** are immutable, game-agnostic descriptions of slots, stacks, protection state, container eligibility, routing distance, and stable order. Item compatibility is an opaque key supplied by the future Valheim adapter; planners never recreate an item from its visible name.
2. **Planners** deterministically produce sort layouts and transfer steps. They may simulate quantities internally, but they never hold or mutate a Valheim, Unity, BepInEx, or network object.
3. **Adapters and executor** will later capture and revalidate real game state, then apply each approved step through Valheim's normal ownership and synchronization APIs. No adapter or executor exists in this milestone.

The sort planner preserves fixed quick-bar, equipped, and exact protected slots, merges only compatible movable stacks, and orders resulting stacks by player-visible name. Storage planning keeps targeted-container and distance ordering explicit, fills compatible partial stacks before opening new destination stacks, reports shortages and leftovers, and conserves item-unit counts.

The responsiveness budget is injected as an interface. Production will use a profiled elapsed-time implementation; deterministic tests use a controlled budget that proves planning stops safely and marks a partial search. This is not a user-visible container cap.

## Safety boundary

The planner output is a proposal, never permission to mutate. A future executor must revalidate source identity and quantity, destination compatibility and capacity, distance, access, ownership, and container availability immediately before every step. One failed container must be skipped without rolling back already completed safe transfers.
