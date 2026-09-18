# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster by **JStack424** combines automatic inventory sorting, deliberate nearby-storage depositing, protected-stack replenishment, storage-aware requirement totals, expedition-kit withdrawal, and optional nearby-chest material use for building and crafting in one Valheim workflow.

## Vanilla-plus by design

Stackmaster is built to preserve the vanilla experience. It adds no gameplay advantage: no free resources, extra carrying capacity, powers, cheats, or progression shortcuts. Instead, it removes repetitive container rummaging so you can spend more time adventuring and building immersive spaces. Put a pantry behind real doors and cook at the nearby station without fetching every ingredient by hand, or unload a mining haul into its organized storage without visiting every chest one by one.

Convenience stays deliberate. Auto-deposit works only when you look at or interact with a chest and press `Left Alt + E`; simply walking near your base never empties your backpack.

> **Testing status:** Stackmaster 1.1.1 is a local compatibility test candidate for Valheim 1.0.14. It carries forward the live-tested 1.1.0 gameplay and configuration unchanged: atomic expedition-kit withdrawal, separate modifier-left-click protection and modifier-right-click restocking controls, reconnect recovery without restarting Valheim, and one-click chest-backed crafting. Automated coverage includes click routing and state transitions, kit planning, inventory-slot and carry-weight simulation, identity-preserving emergency rollback, reservation-before-ownership cleanup ordering, reconnect isolation, crafting interruption cleanup, the independent storage-total and crafting/building permission combinations, and the 30-second ownership-lease safeguards. Broader multiplayer, dedicated-server, workbench-extension, and load/unload edge-case testing remains incomplete; back up valuable characters and worlds and report any item loss, duplication, crash, corrupt item data, blocked chest, or synchronization disagreement.

## Features

- **Inventory sorting**
  - Sorts movable backpack slots alphabetically whenever the inventory opens.
  - Adds an **Auto-sort chest** checkbox beneath each supported opened vanilla chest.
  - Sorts an enabled chest when its UI opens and again when it closes, so removing items does not require a reopen to restore order.
  - Remembers disabled chests locally per player, world, and chest without writing preferences to shared world state or affecting other players.
  - Keeps the whole quick bar, equipped items, and protected stacks fixed.
- **One consistent storage scope**
  - Inside a vanilla workbench build zone, follows every overlapping workbench zone as one connected base mesh and includes loaded supported chests anywhere in that exact union.
  - Excludes nearby chests outside the connected mesh, even if they are within the fallback radius.
  - Outside every workbench mesh, uses the configurable player-centered radius (20 metres by default).
  - Applies the same scope to deposit, replenishment, building, and crafting; a chest's **Auto-sort chest** checkbox never changes storage eligibility.
- **Auto-deposit**
  - Runs only when you press `Left Alt + E` while targeting or interacting with a vanilla chest.
  - Deposits only into eligible vanilla containers in the active storage scope that already hold a compatible item.
  - Fills partial stacks first, prioritizing the targeted chest and then searching nearest to farthest.
  - Leaves unmatched items and overflow safely in the player inventory.
- **Auto-replenish**
  - Refills protected stacks, including ammo and consumables, to optional target quantities during the same storage action.
  - Uses the configured storage-action modifier for two distinct inventory controls: left-click instantly protects or fully unprotects an item, while right-click opens target entry for stackable items.
  - A left-click protection toggle never opens a dialog; unlocking also clears any replenishment target. Canceling the right-click dialog preserves the prior state, and non-stackable items remain protection-only.
  - Keeps protected stacks fixed while sorting and follows one compatible stack when it moves or survives a merge inside the player inventory.
  - Clears a stack's protection and target after that whole stack is manually transferred to a chest or dropped into the world; partial moves keep the protected remainder.
  - Prunes older orphaned targets before a storage hotkey action, so an item you no longer carry does not keep reporting `target item missing`.
- **Storage-aware build and craft requirements**
  - Separately controls storage-backed crafting, storage-backed building, and visibility of base-wide storage totals; all three options default to on.
  - When enabled for an action, counts and consumes exact costs from the player plus accessible vanilla chests in the active storage scope, regardless of current network ownership.
  - Independently shows selected build-piece, crafting, and upgrade costs as `required / total available`, including selected upgrade quality and multi-craft totals.
  - Affordability and red/white flashing always follow what the action may actually consume: player-held materials when its storage permission is off, or combined player-plus-storage stock when it is on.
  - Adaptively fits longer exact crafting and building totals such as `45 / 172` and `999 / 999` inside the vanilla amount label instead of clipping the final digits; short totals retain the normal font size.
  - Handles quality-specific and multi-craft quantities without consuming whole stacks or charging duplicate costs twice.
  - Uses the player inventory first, then chooses only the minimum distinct chest set needed by the complete action plan.
- **Expedition kits from the build menu**
  - Hold the configured storage-action modifier (Left Alt by default) and click a build piece to move one complete copy of that piece's recipe from eligible storage into your inventory.
  - Keeps the build menu open and leaves ordinary clicks unchanged.
  - Treats every modified click as one full additional kit; materials you already carry never reduce the requested quantities.
  - Draws each ingredient from the chest holding the largest total stock first, then smaller sources, with deterministic tie-breaking.
  - Moves nothing unless the complete recipe is available, every required chest remains safe and accessible, all resulting stacks fit, and the added weight stays within carry capacity.
- **Safety**
  - Reads nearby build and craft totals without claiming chest ownership.
  - Requests ownership only for required chests, then rechecks active-scope membership, access, ownership, chest use, serialized revision, stack identity, and quantity before removal.
  - Withdraws exact quantities and rolls back completed steps if a later removal fails.
  - Cancels before consuming anything and shows `The required materials are currently in use` when a required chest is busy.
  - Keeps only ownership demonstrably acquired and used by a successful build on a 30-second sliding lease, yielding immediately when another player manually opens that chest.
  - Ends logical in-use reservations before releasing matching ownership. If exact same-client cleanup fails transiently, it is retained and retried before that ownership can be relinquished, including during disable and hot unload; later or unrelated owners are never cleared.
  - Never mutates inaccessible, unknown, unsupported, or actively used containers.
  - Shows compact totals, shortages, meaningful skips, and incomplete-search notices.
  - Disables all item-changing behavior if its runtime compatibility checks fail.

## Controls

- **Open inventory:** Sort movable player slots when the player-inventory Auto-sort checkbox is enabled. An opened vanilla chest follows its own **Auto-sort chest** checkbox on both open and close.
- **Left Alt + left-click an unprotected item:** Protect it immediately with no restocking target and no dialog.
- **Left Alt + left-click any protected item:** Fully unprotect it and clear any restocking target immediately.
- **Left Alt + right-click a stackable item:** Open the restocking quantity dialog. Confirming protects the item and adds or edits its target; canceling preserves its exact prior protection and target state.
- **Left Alt + right-click a non-stackable item:** Leave its protection state unchanged; non-stackable items cannot have restocking targets.
- **Ordinary inventory clicks:** Keep Valheim's normal left- and right-click behavior.
- **Left Alt + E while targeting a vanilla container:** Deposit matching items and replenish protected targets.
- **Left Alt + E while a vanilla chest is open:** Run the same action using that chest as the target without closing it.
- **Left Alt-click a build piece in the build menu:** Withdraw one complete expedition kit for that piece from eligible storage without closing the menu. If the configured storage-action shortcut uses different modifiers, use those modifiers instead.

Version 1.1.1 supports keyboard and mouse. Controller-specific controls are not included yet.

## Configuration

Stackmaster has exactly six settings:

1. **Auto-sort enabled** — on by default and also controlled by the checkbox below the player inventory. Chest auto-sort is controlled separately in each chest UI and is not a seventh global setting.
2. **Nearby-storage radius** — 20 metres by default; configurable from 1 to 50 metres and used by all chest-powered features only while the player is outside every valid connected workbench mesh.
3. **Storage-action keybind** — Left Alt + E by default. Its modifier keys also activate protected-item left/right clicks in the player inventory and expedition-kit left-clicks in the build menu; the main E key is not required for either click action.
4. **Allow building from storage** — on by default and independently controls building eligibility and consumption from storage.
5. **Allow crafting from storage** — on by default and independently controls crafting/upgrade eligibility and consumption from storage.
6. **Show storage amounts in craft and build menus** — on by default; independently shows player-plus-eligible-storage totals in both requirement UIs without granting permission to consume those stored items.

All six global settings are available through the normal r2modman/BepInEx configuration editor after the first launch. Existing building/crafting opt-outs migrate automatically to the renamed permission settings, so an explicit `false` remains off and the obsolete keys disappear after launch. Per-chest auto-sort choices are local-only preferences scoped to the current player, world, and stable chest identity; a missing or unreadable identity safely skips chest sorting.

## Installation

Install with r2modman, Thunderstore Mod Manager, or another Thunderstore-compatible manager. BepInExPack Valheim is installed as a separate dependency; Stackmaster does not bundle BepInEx or Harmony.

For a manual install, install `denikson-BepInExPack_Valheim` 5.4.2350 or newer, then place `Stackmaster.dll` in `BepInEx/plugins/Stackmaster/`.

## Compatibility and support

Stackmaster 1.1.1 is built and fail-closed for Valheim API 1.0.14 / Steam build 25364309, Unity 6000.0.75f1, BepInEx runtime 5.4.23.5, and Harmony 2.9.0.0. It supports vanilla containers only. A mismatched or unsafe runtime disables Stackmaster before its inventory hooks can change items.

Inside a base, Stackmaster discovers the complete connected union of loaded canonical vanilla workbench build zones using each station's current game-reported build range. Outside a base, the configurable player-centered radius is the fallback. Nearby build/craft totals read stable serialized snapshots from accessible vanilla containers in that active scope without claiming them, including containers owned by another peer. Mutation remains stricter: after player-first allocation, Stackmaster requests ownership only for the minimum required chests, revalidates their owner/data revisions and exact contents, reserves them for the transaction, and cancels before mutation if any required chest is busy, denied, stale, or unavailable. Valheim’s owner-authorized request is asynchronous. Remote-owned building actions use the existing prepared-ownership retry window; chest-backed crafting and expedition-kit clicks instead wait in their client-side coroutines for the exact required ownership and proceed from the original click only after a fresh matching plan and reservations are ready. After a successful chest-backed building placement, only the exact acquired chests actually used remain locally owned on a 30-second sliding lease, renewed by each subsequent successful build use. Crafting and expedition-kit withdrawals release their reservations and exact acquired ownership immediately and never create or renew that lease. A remote player's manual open request immediately invalidates the build lease and continues through vanilla's normal ownership transfer. Every cancellation, failure, rollback, disable, disconnect, logout, unload, shutdown, exception, or unused acquisition releases only ownership Stackmaster can still prove it acquired, with identity, session, owner-revision, and current-owner guards. If exact local reservation cleanup cannot finish immediately, Stackmaster keeps the matching ownership cleanup record and retries the reservation first; it never clears a later or unrelated owner's in-use state.

Plugin GUID: `com.jstack424.stackmaster`

Source, issue tracker, and MIT license: <https://github.com/JStack424/Stackmaster>

## Development

The repository keeps pure inventory policy separate from Valheim/Unity adapters. The deployable plugin targets .NET Framework 4.8, pure planners target .NET Standard 2.0, and automated tests target .NET 8.

Private compile-time references must come from your own Valheim/BepInEx installation and remain under the ignored `lib/local/StackmasterReferences/` directory (or an ignored path override). They are never committed or packaged. On Windows, `scripts/Inspect-StackmasterEnvironment.ps1` performs a read-only environment inventory, and `scripts/Collect-StackmasterReferences.ps1` copies only the required compile-time assemblies from paths you explicitly provide.

```bash
./scripts/build.sh
```

The build restores locked dependencies, builds Release, runs the pure-domain suite, runs repository safety checks, and rejects copied runtime/game assemblies in plugin output. See the [approved behavior contract](docs/DESIGN-QUESTIONNAIRE.md) and [implementation/test plan](docs/NEXT-STEPS.md) for the full design and release gates.
