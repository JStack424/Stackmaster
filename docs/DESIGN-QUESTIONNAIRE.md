# Design Questionnaire

This is the running, finite decision list for the first Valheim quality-of-life mod. It can change as answers reveal genuinely new design areas, but every change to the total will be recorded so the process never becomes an invisible, endless interview.

## Progress

- **Completed:** 18 of 18
- **Current:** Approved; ready for implementation-environment validation
- **Build readiness:** Approved by Joe on September 11, 2026

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
- [x] **11. Loadout replenishment and target quantities** — Define desired stack sizes, how players configure them, where replacement food/ammo comes from, and how shortages or excess are handled.
- [x] **12. Chest controls and exceptions** — Decide how a chest opts in or out, whether it can accept or reject categories, and whether those rules belong to a chest, player, or world.
- [x] **13. Controls, interface, and feedback** — Choose keyboard/controller inputs, inventory buttons, configuration access, HUD summaries, sounds, and error/skip messages.
- [x] **14. Multiplayer and installation contract** — Decide the desired host/client/dedicated-server behavior, who must install the mod, and how simultaneous chest use should be handled.
- [x] **15. Configuration model and defaults** — Decide which settings are exposed, conservative defaults, presets, and whether settings are per-player, per-profile, per-world, or server-controlled.
- [x] **16. Compatibility, performance, and failure safety** — Set expectations for other inventory mods, game updates, scanning cost, rollback-safe behavior, and what the mod does when a patch or transfer cannot be trusted.
- [x] **17. Public package identity** — Choose the mod name, plugin GUID, Thunderstore team/package identity, license, source/homepage plan, icon direction, and public wording.
- [x] **18. Test plan and definition of done** — Agree on solo, host, guest, dedicated-server, conflict, update, packaging, and r2modman tests required before v0.1 is publishable.

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

### 11. Loadout replenishment and target quantities

- Any stackable item can be protected with an optional user-set target quantity and stays in its chosen inventory slot.
- When protecting a stackable item, the player chooses between protection only and protection with replenishment.
- Protection-only items remain fixed and are never deposited, but are not replenished or trimmed to a target.
- Non-stackable protected items remain fixed and protected without a quantity target.
- Each target belongs to that exact protected slot, not to the item type across the player's whole inventory.
- Replenishment fills that protected slot toward its own target without treating loose copies elsewhere in the inventory as satisfying it.
- When the player chooses protection with replenishment, immediately ask for the target quantity rather than inferring it from the stack's current quantity or maximum size.
- Valid targets are bounded by what that single slot can legally hold.
- For replenishment, withdraw compatible items from the targeted chest first, then from other eligible nearby containers from nearest to farthest.
- If nearby storage cannot satisfy the full target, take everything available toward it and leave the protected slot partially replenished rather than making the transfer all-or-nothing.
- Report the remaining shortfall in the action feedback.
- Do not edit a target in place. To change it, the player unprotects the slot, protects it again, and enters a new target in the normal setup prompt.
- If a protected slot contains more than its target, deposit the excess through the normal matching-container routing and leave exactly the target quantity in that slot.

### 12. Chest controls and exceptions

- All eligible nearby containers participate automatically in both depositing and replenishment.
- Do not add per-container opt-in or opt-out controls.
- Do not add item-type or category filters; the matching-content rule is the only routing rule a container needs.
- Containers that Valheim considers locked, private, inaccessible, or unavailable remain excluded under the general eligibility rules from Question 8.

### 13. Controls, interface, and feedback

- The targeted-container tooltip advertises the deposit/replenish action.
- On keyboard, trigger the combined deposit/replenish action by holding Left Alt + E while targeting a valid container.
- This deliberately extends Valheim's familiar hold-E container behavior rather than adding an unrelated standalone key.
- Keep the binding configurable.
- In the inventory UI, Left Alt-clicking an item or slot opens its protection choices: protect only, protect with replenishment target when stackable, or unprotect when already protected.
- The auto-sort setting appears as an in-game checkbox.
- v0.1 supports keyboard and mouse only; controller bindings and controller-specific UI are outside the first-release scope.
- All quantities in the result popup count individual item units, not stacks or distinct item types.
- Keep the popup compact: show aggregate totals for deposited, replenished, and left behind, while naming any item-specific replenishment shortages and meaningful skipped items.
- Do not show a full item-by-item success breakdown.
- Do not add success or failure sounds; feedback stays visual.
- If the action cannot run, use the same popup to show a concise reason.

### 14. Multiplayer and installation contract

- The mod is optional and client-side from an installation perspective: only players who want its features install it.
- Players with the mod can join and use ordinary vanilla-hosted and dedicated servers; the host, server, and other players do not need the mod.
- Container mutation must still use Valheim's normal ownership and synchronization behavior safely.
- If another player is actively using a nearby container, skip it rather than waiting, retrying, or modifying it concurrently.
- Include skipped in-use containers in the compact action feedback.

### 15. Configuration model and defaults

- Keep v0.1 configuration focused on exactly three settings: auto-sort enabled, nearby-storage radius, and the storage-action keybind.
- Auto-sort defaults to enabled for a new r2modman profile.
- The nearby-storage radius defaults to 20 meters.
- The storage-action binding defaults to Left Alt + E.
- Do not add separate deposit/replenishment toggles, presets, or an advanced-settings section in v0.1.
- Put the auto-sort checkbox directly in the inventory UI.
- Expose the radius and keybinding through the normal r2modman/BepInEx configuration rather than building a separate in-game settings panel for them.
- Store one set of these settings per r2modman profile; all characters and worlds launched through that profile use the same values.
- Protected-slot choices and replenishment targets belong to the individual Valheim character and follow that character across every world.

### 16. Compatibility, performance, and failure safety

- The action must never duplicate, delete, or corrupt items, and unsupported or unavailable storage must fail safely.
- Nearby-container discovery and routing run only when needed rather than continuously scanning the world.
- Limit v0.1 support to vanilla Valheim containers. Do not claim automatic compatibility with modded storage or ship one-off patches for other storage mods yet.
- Unknown or modded container types are excluded rather than being modified speculatively.
- If one container fails during a storage action, skip that container, continue safely with the others, and report the failure in the result popup.
- Completed safe transfers remain completed; do not attempt a risky whole-action rollback.
- Use a time budget rather than a fixed chest-count cap when a large configured radius contains many containers.
- Process containers in the already-set routing order until the action reaches its responsiveness budget, then stop safely and report that the action ended before every eligible container was checked.
- Choose the exact budget during profiling so the action avoids noticeable gameplay hitches across the supported test machines; do not expose it as a fourth user setting.
- If a Valheim update or compatibility check shows that any part of the mod is unsafe, disable the entire mod for that session before it can alter items.
- Show the player a clear incompatibility warning and write the technical reason to the BepInEx log; do not keep trying old behavior or run only a subset of features.

### 17. Public package identity

- Public mod name: **Stackmaster**.
- Reserve the matching package and assembly naming direction (`Stackmaster`) unless Thunderstore's final creation flow reveals a collision.
- Thunderstore author/team identity: **JStack424**.
- BepInEx plugin GUID: `com.jstack424.stackmaster`.
- License: MIT, allowing reuse and modification with the required copyright and license notice.
- Publish the source in a public GitHub repository and enable GitHub Issues for bug reports and support.
- Use that repository as Stackmaster's Thunderstore homepage/source link; choose the exact GitHub owner and create the remote only when Joe approves publication.
- Icon direction: a clean, readable square mark centered on neatly stacked Viking-style wooden chests, using original artwork rather than Valheim assets.
- Thunderstore lead: “Turn a messy Viking inventory into a tidy, adventure-ready loadout.”

### 18. Test plan and definition of done

- Required real gameplay environments before v0.1 publication:
  - Solo world.
  - Player hosting a co-op world.
  - Non-host guest in a co-op world.
  - Vanilla dedicated server, with no server-side Stackmaster installation.
- Each multiplayer test must verify that unmodded peers remain compatible and that chest state stays synchronized.
- v0.1 uses a basic smoke-test release gate rather than an exhaustive strict safety campaign.
- The smoke test exercises sorting, depositing, and replenishment in representative conditions across every required environment. Any observed item loss, duplication, crash, or synchronization error blocks release, but the gate does not claim proof that no rare defect exists beyond the tested paths.
- Mod-conflict testing is limited to a clean r2modman profile containing BepInEx and Stackmaster's declared dependencies; v0.1 makes no broader coexistence promise for other gameplay or inventory mods.
- Packaging gate: the release ZIP must install successfully into a fresh r2modman profile and launch with its declared dependencies and intended defaults.
- Updating an older package and uninstalling cleanly are not required release-gate tests for v0.1.
- Persistence gate: after protecting slots and setting replenishment targets, fully quit and relaunch Valheim with the same character and verify those choices remain intact.
- Cross-world persistence and separate-character isolation are part of the intended behavior but are not separate required release-gate tests for v0.1.
- Automated tests for deterministic sorting and item-transfer logic must pass before release, including protected-slot behavior, partial-stack priority, routing order, replenishment, shortages, excess handling, and no item-count drift.
- Performance testing is limited to one normal-sized base representing ordinary play within the configured 20-meter radius. The action must remain acceptably responsive there, and profiling from that run sets the internal time budget.
- A deliberately oversized stress base is not a separate v0.1 release gate, but automated tests must verify that reaching the time budget stops safely and produces the promised partial-search notice.
- The supported Valheim version must be recorded from the installed game. Before claiming compatibility after a later game update, rerun the same smoke gate; automated coverage must also verify that a failed compatibility check disables all item-changing behavior and surfaces the warning.
- The exact release ZIP must pass structural validation: required Thunderstore files present, 256×256 PNG icon, valid manifest and dependency versions, matching plugin/package version, and no game assemblies, machine-local paths, credentials, test binaries, or development debris.

All 18 checkpoints are complete. Joe approved the consolidated behavior contract on September 11, 2026. Implementation may begin with environment validation; any material behavior change must return to Joe for approval.

## Deferred decisions

None yet.

## List changes

- Initial list created with 17 checkpoints.
- After Question 1, expanded from 17 to 18 because loadout replenishment with target quantities is a distinct core system, not merely an item-protection detail.
