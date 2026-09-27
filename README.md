# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster by **JStack424** brings inventory sorting, **Quick Stack**, and building and crafting from nearby storage into one Valheim workflow.

> **Test-candidate status:** 1.3.3 refines Quick Grab reservations and their strip. A valid modified build-piece click saves one reservation even when the complete material set is unavailable, while material movement remains strictly all-or-nothing. Each successful ordinary cost-bearing placement of that exact reserved piece consumes one count; failed/cancelled, preview, creative/no-cost, and unrelated paths do not. The strip measures its rendered label before icons begin and moves 12 UI pixels right and 10 UI pixels up for a clearer margin. This local test build still requires live Valheim validation, and 1.2.0 remains the production release.

## About this project

I'm a Valheim-loving software engineer with an interest in mild game design. I built Stackmaster with the assistance of AI as a personal quality-of-life project: smooth out the frustrating bits, preserve the developers' intended experience, and never make progression feel cheesed. I didn't set out to build a widely used mod, but it's been lovely to see people enjoying it. If you've been using Stackmaster and enjoy it, please share any issues or requests through [GitHub Issues](https://github.com/JStack424/Stackmaster/issues). I keep playing, testing, and looking for further improvements, so I'll take thoughtful ideas into account.

## Vanilla-plus by design

Stackmaster removes repetitive container management without adding free resources, carrying capacity, powers, cheats, or progression shortcuts. Quick Stack stays deliberate: nothing leaves your backpack just because you walk near a base, and every build or craft still consumes real materials from your inventory or accessible storage.

## TL;DR

- Automatically sort your backpack and opted-in vanilla chests.
- Use **Quick Stack** to deposit matching items and replenish protected targets with one deliberate shortcut.
- Keep durable per-player, per-world material reservations from valid **Quick Grab Materials** clicks, even away from supplies.
- Build and craft using materials from accessible chests.

## Controls

### Inventory

| Control | Behavior |
| --- | --- |
| Open inventory | Sort movable player slots when player Auto-sort is enabled. |
| `Left Alt` + left-click an unprotected item | Protect it immediately, with no restocking target or dialog. |
| `Left Alt` + left-click any protected item | Fully unprotect it and clear its restocking target. |
| `Left Alt` + right-click a stackable item | Open the restocking quantity dialog. Confirming protects the item and adds or edits its target; canceling preserves the previous state. |
| `Left Alt` + right-click a non-stackable item | Leave its protection unchanged. Non-stackable items cannot have restocking targets. |
| Ordinary inventory clicks | Keep Valheim's normal left- and right-click behavior. |

### Chests

| Control | Behavior |
| --- | --- |
| Open or close a vanilla chest | Sort that chest when its **Auto-sort chest** setting is enabled. |
| `Left Alt + E` while targeting a vanilla container | Run **Quick Stack**: deposit matching items and replenish protected targets. |
| `Left Alt + E` while a vanilla chest is open | Run **Quick Stack** using that chest as the target without closing it. |

### Build menu

| Control | Behavior |
| --- | --- |
| `Left Alt` + click a build piece | Add one persistent reservation, then grab one complete material set when it is safely available, without closing the build menu. |

The modifier follows the configured **Quick Stack** shortcut, which uses `Left Alt + E` by default. While the build menu is open, its keyboard/mouse action list shows **Quick Grab Materials** with **Left Alt + Click** by default and updates that shortcut text when the configured modifiers change. Version 1.2.0 does not add a controller-specific action.

## Detailed mechanics

- **Inventory sorting**
  - Sorts movable backpack slots alphabetically whenever the inventory opens.
  - Adds an **Auto-sort chest** checkbox beneath each supported opened vanilla chest.
  - Sorts an enabled chest when its UI opens and again when it closes, and once after a Quick Stack that actually deposits into it.
  - Remembers disabled chests locally per player, world, and chest without writing preferences to shared world state or affecting other players.
  - Keeps the whole quick bar, equipped items, and protected stacks fixed.
- **One consistent storage scope**
  - Inside a vanilla workbench build zone, follows every overlapping workbench zone as one connected base mesh and includes loaded supported chests anywhere in that exact union.
  - Excludes nearby chests outside the connected mesh, even if they are within the fallback radius.
  - Outside every workbench mesh, uses the configurable player-centered radius (20 metres by default).
  - Applies the same scope to Quick Stack, building, and crafting; a chest's **Auto-sort chest** checkbox never changes storage eligibility.
- **Quick Stack**
  - Runs only when you press `Left Alt + E` while targeting or interacting with a vanilla chest.
  - Completely ignores carried items whose canonical Valheim maximum stack size is 1 or less. Items such as cultivators stay in the backpack, never route to matching or remembered chests, never receive failed-deposit warnings, and never count as left behind.
  - Refills protected stacks, including ammo and consumables, to optional target quantities, then deposits eligible carried items.
  - Also retains and replenishes the material quantities required by current Quick Grab Materials reservations. Reservation requirements are additive: a personal target of 50 wood plus reservations requiring 20 wood keeps 70 total.
  - Keeps explicit personal targets in their fixed protected slots and assigns separate reservation quantities only to movable stacks or legal empty backpack slots.
  - If the backpack begins full, an ordinary eligible deposit can free a slot which the same validated plan then uses for reservation replenishment.
  - Fills current matching stacks first, prioritizing the targeted chest and then searching nearest to farthest.
  - Remembers the last directly opened or targeted chest for each exact item type. If no current matching chest accepts any quantity, Quick Stack can return that item to its remembered chest even after the chest reaches zero stock.
  - A remembered destination is only a local hint: it must still be loaded, inside the active scope, accessible, idle, compatible, and exactly the same network chest at execution time. Missing, destroyed, unloaded, blocked, busy, full, or otherwise ineligible destinations leave the item safely in the player inventory.
  - Current matching destinations always win. If one accepts even part of a stack, Quick Stack does not open a second route to the remembered chest for the remainder.
  - Marks only the eligible quantity that survived a failed or partial deposit with a red border and `!quantity`; quick-bar, equipped, protection-only, and target-retained quantities are excluded. The warning follows the surviving item through sorting and clears when that exact item is hovered once or the inventory closes.
  - Sorts each distinct destination chest that actually accepted a deposit at most once at the safe end of the action, and only when that chest’s existing **Auto-sort chest** preference is enabled. Failed, untouched, unavailable, and disabled destinations are skipped.
  - Shows every result as a short three-line bullet list for deposited, replenished, and left-behind quantities, including zeros.
  - Leaves unmatched items and overflow safely in the player inventory.
  - Uses the configured Quick Stack modifier for two distinct inventory controls: left-click instantly protects or fully unprotects an item, while right-click opens target entry for stackable items.
  - A left-click protection toggle never opens a dialog; unlocking also clears any replenishment target. Canceling the right-click dialog preserves the prior state, and non-stackable items remain protection-only.
  - Keeps protected stacks fixed while sorting and follows one compatible stack when it moves or survives a merge inside the player inventory.
  - Clears a stack's protection and target after that whole stack is manually transferred to a chest or dropped into the world; partial moves keep the protected remainder.
  - Prunes older orphaned targets before a Quick Stack action, so an item you no longer carry does not keep reporting `target item missing`.
- **Storage-aware build and craft requirements**
  - Separately controls storage-backed crafting, storage-backed building, and visibility of base-wide storage totals; all three options default to on.
  - When enabled for an action, counts and consumes exact costs from the player plus accessible vanilla chests in the active storage scope, regardless of current network ownership.
  - Independently shows selected build-piece, crafting, and upgrade costs as `required / total available`, including selected upgrade quality and multi-craft totals.
  - Affordability and red/white flashing always follow what the action may actually consume: player-held materials when its storage permission is off, or combined player-plus-storage stock when it is on.
  - Adaptively fits longer exact crafting and building totals such as `45 / 172` and `999 / 999` inside the vanilla amount label instead of clipping the final digits; short totals retain the normal font size.
  - Handles quality-specific and multi-craft quantities without consuming whole stacks or charging duplicate costs twice.
  - Uses the player inventory first, then chooses only the minimum distinct chest set needed by the complete action plan.
- **Quick Grab Materials**
  - Hold the configured Quick Stack modifier (Left Alt by default) and click a build piece to add one persistent reservation and attempt to grab one complete material set from eligible storage.
  - Keeps the build menu open and leaves ordinary clicks unchanged.
  - Treats every valid modified click as one additional reservation. Repeated clicks increment it deterministically, including while away from supplies.
  - Saves the reservation before any shared inventory can change. Earlier reservation state migrates from PlayerPrefs on the first mutation; new state is flushed to a detached file and published with an atomic same-directory replacement. Malformed, mixed-invalid, or changed recipe identity and failed durable saves remain fail-closed, create no active or durable phantom reservation, and move no materials.
  - Draws each ingredient from the chest holding the largest total stock first, then smaller sources, with deterministic tie-breaking.
  - Moves nothing unless the complete recipe is available, every required chest remains safe and accessible, all resulting stacks fit, and the added weight stays within carry capacity. Zero stock, partial stock, unreachable storage, ownership failure, or capacity failure keeps the saved reservation but performs no partial transfer.
  - Clearly reports either `grabbed materials and added reservation` or `reservation added without materials`; reservation-only clicks do not create failed-deposit warnings.
  - Shows reserved-piece icons and counts in a horizontal strip immediately to the right of the player inventory, shifted 10 UI pixels above its top edge and clipped to the available safe screen width. Clicking one icon releases exactly one reservation and never moves items or starts Quick Stack.
  - After a genuinely successful local placement, the exact placed prefab consumes one reservation count: `Wall ×5` becomes `Wall ×4`, the final count removes the row, and unrelated piece variants remain untouched. Failed/cancelled placement, previews, creative/no-cost placement, repair, dismantle, crafting, other players, and duplicate completion callbacks do not decrement anything. If the durable decrement cannot be saved, the completed world build remains but the reservation is deliberately left intact and Stackmaster shows one restrained warning.
  - Clicking a reservation icon remains a separate explicit one-count release; it never moves materials or starts Quick Stack.
  - Marks only the exact reservation-served share in orange with a plain upper-left quantity matching the personal-target label layout. Player sorting places every positive reservation-backed stack at the front of the movable sortable region before ordinary stacks; fixed quick-bar, equipped, and protected slots remain untouched.
- **Safety**
  - Reads nearby build and craft totals without claiming chest ownership.
  - Requests ownership only for required chests, then rechecks active-scope membership, access, ownership, chest use, serialized revision, stack identity, and quantity before removal.
  - Withdraws exact quantities and rolls back completed steps if a later removal fails.
  - Cancels before consuming anything and shows `The required materials are currently in use` when a required chest is busy.
  - Keeps only ownership demonstrably acquired and used by a successful build on a 30-second sliding lease, yielding immediately when another player manually opens that chest.
  - Ends logical in-use reservations before releasing matching ownership. If exact same-client cleanup fails transiently, it is retained and retried before that ownership can be relinquished, including during disable and hot unload; later or unrelated owners are never cleared.
  - Never mutates inaccessible, unknown, unsupported, or actively used containers.
  - Shows compact totals, shortages, meaningful skips, and incomplete-search notices.
  - Treats malformed or unavailable reservation state as a fail-closed condition before reservation-aware item movement or player sorting.
  - Keeps reservation-row and orange-highlight failures isolated from item state and from the existing blue protection overlays.
  - Disables all item-changing behavior if its runtime compatibility checks fail.

## Configuration

Stackmaster has exactly six settings:

1. **Auto-sort enabled** — on by default and also controlled by the checkbox below the player inventory. Chest auto-sort is controlled separately in each chest UI and is not a seventh global setting.
2. **Nearby-storage radius** — 20 metres by default; configurable from 1 to 50 metres and used by all chest-powered features only while the player is outside every valid connected workbench mesh.
3. **Quick Stack keybind** — Left Alt + E by default. Existing custom shortcuts are migrated automatically from the legacy setting name. Its modifier keys also activate protected-item left/right clicks in the player inventory and Quick Grab Materials left-clicks in the build menu; the main E key is not required for either click action.
4. **Allow building from storage** — on by default and independently controls building eligibility and consumption from storage.
5. **Allow crafting from storage** — on by default and independently controls crafting/upgrade eligibility and consumption from storage.
6. **Show storage amounts in craft and build menus** — on by default; independently shows player-plus-eligible-storage totals in both requirement UIs without granting permission to consume those stored items.

All six global settings are available through the normal r2modman/BepInEx configuration editor after the first launch. Existing building/crafting opt-outs migrate automatically to the renamed permission settings, so an explicit `false` remains off and the obsolete keys disappear after launch. Per-chest auto-sort choices are local-only preferences scoped to the current player, world, and stable chest identity; a missing or unreadable identity safely skips chest sorting.

## Installation

Install with r2modman, Thunderstore Mod Manager, or another Thunderstore-compatible manager. BepInExPack Valheim is installed as a separate dependency; Stackmaster does not bundle BepInEx or Harmony.

For a manual install, install `denikson-BepInExPack_Valheim` 5.4.2350 or newer, then place `Stackmaster.dll` in `BepInEx/plugins/Stackmaster/`.

## Compatibility and support

> **Stackmaster 1.2.0:** Quick Stack now remembers the last directly observed chest for each exact item type, including destinations that have reached zero stock, and safely leaves items carried when that chest is unavailable, inaccessible, busy, or full. Genuine failed or partial deposits mark only the surviving eligible quantity in red until that exact item is hovered once or the inventory closes. Results are shown as a compact three-line bullet list. This release keeps the straightforward requirement-display behavior from before the unsuccessful optimization experiment and makes no performance-improvement claim. Joe live-tested and approved the new Quick Stack behavior. Broader multiplayer, dedicated-server, workbench-extension, manual-access-preemption, and load/unload edge-case testing remains incomplete. Back up valuable characters and worlds and report any item loss, duplication, free output, crash, blocked chest, synchronization disagreement, stale warning, or stale requirement total.

Stackmaster 1.2.0 is compiled from a documented Valheim/Unity/BepInEx/Harmony reference bundle, but exact version strings, file hashes, and assembly MVIDs are provenance and diagnostics—not a runtime allowlist. It supports vanilla containers only. On startup, Stackmaster checks the complete API surface it relies on, including exact overload parameters and every Harmony target, before installing any patch. A missing, changed, or ambiguous contract disables the mod before item-changing hooks are installed; a patching error disables the runtime and removes all Stackmaster patches.

Inside a base, Stackmaster discovers the complete connected union of loaded canonical vanilla workbench build zones using each station's current game-reported build range. Outside a base, the configurable player-centered radius is the fallback. Nearby build/craft totals read stable serialized snapshots from accessible vanilla containers in that active scope without claiming them, including containers owned by another peer. Mutation remains stricter: after player-first allocation, Stackmaster requests ownership only for the minimum required chests, revalidates their owner/data revisions and exact contents, reserves them for the transaction, and cancels before mutation if any required chest is busy, denied, stale, or unavailable. Valheim’s owner-authorized request is asynchronous. Remote-owned building actions use the existing prepared-ownership retry window; chest-backed crafting and Quick Grab Materials clicks instead wait in their client-side coroutines for the exact required ownership and proceed from the original click only after a fresh matching plan and reservations are ready. After a successful chest-backed building placement, only the exact acquired chests actually used remain locally owned on a 30-second sliding lease, renewed by each subsequent successful build use. Crafting and quick-grab withdrawals release their reservations and exact acquired ownership immediately and never create or renew that lease. A remote player's manual open request immediately invalidates the build lease and continues through vanilla's normal ownership transfer. Every cancellation, failure, rollback, disable, disconnect, logout, unload, shutdown, exception, or unused acquisition releases only ownership Stackmaster can still prove it acquired, with identity, session, owner-revision, and current-owner guards. If exact local reservation cleanup cannot finish immediately, Stackmaster keeps the matching ownership cleanup record and retries the reservation first; it never clears a later or unrelated owner's in-use state.

Plugin GUID: `com.jstack424.stackmaster`

Source, issue tracker, and MIT license: <https://github.com/JStack424/Stackmaster>

## Development

The repository keeps pure inventory policy separate from Valheim/Unity adapters. The deployable plugin targets .NET Framework 4.8, pure planners target .NET Standard 2.0, and automated tests target .NET 8.

Private compile-time references must come from your own Valheim/BepInEx installation and remain under the ignored `lib/local/StackmasterReferences/` directory (or an ignored path override). They are never committed or packaged. On Windows, `scripts/Inspect-StackmasterEnvironment.ps1` performs a read-only environment inventory, and `scripts/Collect-StackmasterReferences.ps1` copies only the required compile-time assemblies from paths you explicitly provide.

```bash
./scripts/build.sh
```

The build restores locked dependencies, builds Release, runs the pure-domain suite, runs repository safety checks, and rejects copied runtime/game assemblies in plugin output. See the [approved behavior contract](docs/DESIGN-QUESTIONNAIRE.md) and [implementation/test plan](docs/NEXT-STEPS.md) for the full design and release gates.
