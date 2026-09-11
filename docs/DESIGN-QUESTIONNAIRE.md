# Design Questionnaire

This is the running, finite decision list for the first Valheim quality-of-life mod. It can change as answers reveal genuinely new design areas, but every change to the total will be recorded so the process never becomes an invisible, endless interview.

## Progress

- **Completed:** 10 of 18
- **Current:** Question 11 — Loadout replenishment and target quantities
- **Build readiness:** Not ready yet

## How the interview works

- Ask one primary question at a time.
- Label every prompt `Question X of 18` and show `Completed: Y/18`.
- Keep small clarifications inside the current checkpoint rather than inflating the total.
- Add a new checkpoint only when an answer reveals a materially new product decision. If that happens, explain why and update the visible total immediately.
- Record each settled answer here, including defaults, exceptions, and anything intentionally deferred.
- A checkpoint is complete only when the answer is specific enough to implement and test.
- After the final checkpoint, summarize the complete behavior contract for Joe's approval before writing gameplay code.

## Master list

- [x] **1. Ideal gameplay flow** — Describe what should happen from the player's point of view during ordinary adventuring, returning to base, opening inventory, and using storage.
- [x] **2. First-release boundary** — Decide the must-have behavior for v0.1, what can wait, and whether sorting, depositing, and replenishment ship together initially.
- [x] **3. Sorting trigger and timing** — Decide what “always sorted” means in practice: after pickups, inventory changes, opening the inventory, explicit input, or another event model.
- [x] **4. Sort scope and protected areas** — Decide which inventories and slots may move, including hotbar, equipped items, arbitrary protected slots, and an open container.
- [x] **5. Sort order and grouping** — Define the category order, within-category order, naming basis, quality/durability handling, and stable tie-breakers.
- [x] **6. Stack consolidation** — Decide whether sorting also merges partial stacks and how stack limits, item metadata, and exceptional items should behave.
- [x] **7. Deposit trigger and timing** — Decide exactly when nearby-chest stacking or depositing runs and whether automatic behavior is opt-in.
- [x] **8. Eligible storage and search area** — Define container types, search radius or area, access rules, carts/ships/personal chests, and what counts as “nearby.”
- [x] **9. Deposit and routing rules** — Decide matching stacks versus empty slots, destination priority, overflow behavior, and whether chest names or tags control routing.
- [x] **10. Inventory keep and ignore rules** — Define items, categories, slots, minimum quantities, equipped gear, consumables, and other things that must remain with the player.
- [ ] **11. Loadout replenishment and target quantities** — Define desired stack sizes, how players configure them, where replacement food/ammo comes from, and how shortages or excess are handled.
- [ ] **12. Chest controls and exceptions** — Decide how a chest opts in or out, whether it can accept or reject categories, and whether those rules belong to a chest, player, or world.
- [ ] **13. Controls, interface, and feedback** — Choose keyboard/controller inputs, inventory buttons, configuration access, HUD summaries, sounds, and error/skip messages.
- [ ] **14. Multiplayer and installation contract** — Decide the desired host/client/dedicated-server behavior, who must install the mod, and how simultaneous chest use should be handled.
- [ ] **15. Configuration model and defaults** — Decide which settings are exposed, conservative defaults, presets, and whether settings are per-player, per-profile, per-world, or server-controlled.
- [ ] **16. Compatibility, performance, and failure safety** — Set expectations for other inventory mods, game updates, scanning cost, rollback-safe behavior, and what the mod does when a patch or transfer cannot be trusted.
- [ ] **17. Public package identity** — Choose the mod name, plugin GUID, Thunderstore team/package identity, license, source/homepage plan, icon direction, and public wording.
- [ ] **18. Test plan and definition of done** — Agree on solo, host, guest, dedicated-server, conflict, update, packaging, and r2modman tests required before v0.1 is publishable.

## Settled answers

### 1. Ideal gameplay flow

Joe returns from adventuring, approaches and looks directly at a chest, and presses one hotkey. That single action should organize the player's loadout in both directions:

- Deposit unwanted collected items into correctly organized nearby chests where matching item types already exist.
- Search only while the player is deliberately targeting a chest, so storage is not accessible remotely from arbitrary distance.
- Add the action to the targeted chest's interaction tooltip alongside the normal open prompt.
- Never remove items from the quick bar, equipped items, or user-protected items.
- Let the player mark inventory items or slots as kept/favorited/held from the inventory UI, likely through an Alt-click interaction.
- Keep protected items in static player-chosen positions and exclude them from sorting.
- Let protected consumables such as food and ammunition carry a desired stack quantity.
- During the same hotkey action, deposit any amount above each desired quantity and withdraw enough from nearby storage to replenish any shortage.
- The intended result is one deliberate button press that unloads gathered materials and restores the player's chosen adventuring loadout.

Details such as exact modifier keys, radius, routing priority, shortage behavior, and storage eligibility remain assigned to their later checkpoints.

### 2. First-release boundary

The first public version will ship the complete core loop together: inventory sorting, nearby-chest depositing, and loadout replenishment to desired quantities. Replenishment is not deferred to a later release.

### 3. Sorting trigger and timing

- When automatic sorting is enabled, sort the player's inventory once each time the inventory UI is opened.
- Do not continuously reorder items after every pickup or inventory change.
- Add an in-game checkbox that immediately enables or disables automatic inventory sorting without requiring an external configuration editor.
- The checkbox's exact placement, wording, and persistence will be finalized in the controls/interface and configuration checkpoints.

### 4. Sort scope and protected areas

- Automatically sort both the player's movable inventory area and the chest that is opened.
- The player quick bar, equipped items, and user-favorited/held items remain protected, fixed in their chosen slots, and excluded from sorting.
- Opening a chest should organize that chest automatically rather than requiring a separate chest-sort button.
- Chest-specific exclusions and category rules remain assigned to the chest-controls checkpoint.

### 5. Sort order and grouping

- Use the same simple alphabetical order for both the player's movable inventory and opened chests.
- Sort by the item name shown to the player, with no category grouping or custom category sequence.
- Locked/favorited items remain fixed and do not participate in ordering.
- Equal names should use a stable deterministic tie-breaker so repeated sorting does not visibly shuffle items.

### 6. Stack consolidation

- Merge compatible stacks of the same item whenever the game allows it, respecting normal maximum stack sizes and item metadata.
- Never move from, merge into, or otherwise alter a favorited/locked partial stack.
- Items that the game does not consider stack-compatible remain separate even if their visible names match.

### 7. Deposit trigger and timing

- Depositing and replenishment are one explicit hotkey action, not an automatic side effect of opening inventory or storage.
- The action is available only while the player is deliberately looking at a valid chest.
- The targeted chest's interaction tooltip should advertise the hotkey alongside the normal open control.

### 8. Eligible storage and search area

- Search outward from the player's character, not from the targeted chest.
- Use a configurable radius with a 20-meter default.
- Include every nearby container that the game recognizes as accessible to that player, rather than limiting the feature to stationary player-built chests.
- This includes eligible carts, ships, personal storage, and world containers when the player can legitimately access them.
- Skip anything the game treats as locked, private to someone else, inaccessible, or otherwise unavailable for mutation.

### 9. Deposit and routing rules

- Deposit an item only into nearby containers that already hold a stack-compatible item of that type.
- If no eligible nearby container already holds the item, leave it in the player's inventory rather than claiming unrelated empty storage.
- Complete every compatible partial stack before creating a new stack anywhere.
- Within the partial-stack pass, fill partial stacks in the targeted chest first, then work outward through other eligible matching containers from nearest to farthest. If a container has multiple compatible partial stacks, use a stable slot order.
- Only after all compatible partial stacks are full may remaining items start new stacks. For those new stacks, use the targeted chest first when it is an eligible matching container with room, then the nearest eligible matching container.
- Any quantity that cannot fit remains in the player's inventory.
- After the hotkey finishes, show a brief popup with the number of items deposited and the number left behind/not deposited. Final wording and whether counts mean units, stacks, or item types remain for Question 13.

### 10. Inventory keep and ignore rules

- Quick-bar items, equipped items, and user-favorited/held items are protected and never deposited.
- Every other item in the player's movable inventory area is eligible for deposit when a compatible destination exists.
- Do not add a separate item or category ignore list.
- Target-quantity loadout items and their excess/shortage behavior are handled by Question 11.

## Current checkpoint notes

### Question 11 — loadout replenishment and target quantities

- Protected food and ammunition can carry user-set target quantities and stay in their chosen inventory slots.
- Still to settle: whether a target belongs to a specific protected slot or applies to that item type across the whole inventory, plus target editing, source priority, shortage behavior, and excess handling.

## Deferred decisions

None yet.

## List changes

- Initial list created with 17 checkpoints.
- After Question 1, expanded from 17 to 18 because loadout replenishment with target quantities is a distinct core system, not merely an item-protection detail.
