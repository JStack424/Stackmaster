# Design Questionnaire

This is the running, finite decision list for the first Valheim quality-of-life mod. It can change as answers reveal genuinely new design areas, but every change to the total will be recorded so the process never becomes an invisible, endless interview.

## Progress

- **Completed:** 0 of 17
- **Current:** Question 1 — Ideal gameplay flow
- **Build readiness:** Not ready yet

## How the interview works

- Ask one primary question at a time.
- Label every prompt `Question X of 17` and show `Completed: Y/17`.
- Keep small clarifications inside the current checkpoint rather than inflating the total.
- Add a new checkpoint only when an answer reveals a materially new product decision. If that happens, explain why and update the visible total immediately.
- Record each settled answer here, including defaults, exceptions, and anything intentionally deferred.
- A checkpoint is complete only when the answer is specific enough to implement and test.
- After the final checkpoint, summarize the complete behavior contract for Joe's approval before writing gameplay code.

## Master list

- [ ] **1. Ideal gameplay flow** — Describe what should happen from the player's point of view during ordinary adventuring, returning to base, opening inventory, and using storage.
- [ ] **2. First-release boundary** — Decide the must-have behavior for v0.1, what can wait, and whether sorting and storage ship together initially.
- [ ] **3. Sorting trigger and timing** — Decide what “always sorted” means in practice: after pickups, inventory changes, opening the inventory, explicit input, or another event model.
- [ ] **4. Sort scope and protected areas** — Decide which inventories and slots may move, including hotbar, equipped items, arbitrary protected slots, and an open container.
- [ ] **5. Sort order and grouping** — Define the category order, within-category order, naming basis, quality/durability handling, and stable tie-breakers.
- [ ] **6. Stack consolidation** — Decide whether sorting also merges partial stacks and how stack limits, item metadata, and exceptional items should behave.
- [ ] **7. Deposit trigger and timing** — Decide exactly when nearby-chest stacking or depositing runs and whether automatic behavior is opt-in.
- [ ] **8. Eligible storage and search area** — Define container types, search radius or area, access rules, carts/ships/personal chests, and what counts as “nearby.”
- [ ] **9. Deposit and routing rules** — Decide matching stacks versus empty slots, destination priority, overflow behavior, and whether chest names or tags control routing.
- [ ] **10. Inventory keep and ignore rules** — Define items, categories, slots, minimum quantities, equipped gear, consumables, and other things that must remain with the player.
- [ ] **11. Chest controls and exceptions** — Decide how a chest opts in or out, whether it can accept or reject categories, and whether those rules belong to a chest, player, or world.
- [ ] **12. Controls, interface, and feedback** — Choose keyboard/controller inputs, inventory buttons, configuration access, HUD summaries, sounds, and error/skip messages.
- [ ] **13. Multiplayer and installation contract** — Decide the desired host/client/dedicated-server behavior, who must install the mod, and how simultaneous chest use should be handled.
- [ ] **14. Configuration model and defaults** — Decide which settings are exposed, conservative defaults, presets, and whether settings are per-player, per-profile, per-world, or server-controlled.
- [ ] **15. Compatibility, performance, and failure safety** — Set expectations for other inventory mods, game updates, scanning cost, rollback-safe behavior, and what the mod does when a patch or transfer cannot be trusted.
- [ ] **16. Public package identity** — Choose the mod name, plugin GUID, Thunderstore team/package identity, license, source/homepage plan, icon direction, and public wording.
- [ ] **17. Test plan and definition of done** — Agree on solo, host, guest, dedicated-server, conflict, update, packaging, and r2modman tests required before v0.1 is publishable.

## Settled answers

None yet.

## Deferred decisions

None yet.

## List changes

- Initial list created with 17 checkpoints.
