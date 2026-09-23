# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster by **JStack424** brings inventory sorting, automatic chest depositing, and building and crafting from nearby storage into one Valheim workflow.

## About this project

I'm a Valheim-loving software engineer with an interest in mild game design. I built Stackmaster with the assistance of AI as a personal quality-of-life project: smooth out the frustrating bits, preserve the developers' intended experience, and never make progression feel cheesed. I didn't set out to build a widely used mod, but it's been lovely to see people enjoying it. If you've been using Stackmaster and enjoy it, please share any issues or requests through [GitHub Issues](https://github.com/JStack424/Stackmaster/issues). I keep playing, testing, and looking for further improvements, so I'll take thoughtful ideas into account.

## Vanilla-plus by design

Stackmaster removes repetitive container management without adding free resources, carrying capacity, powers, cheats, or progression shortcuts. Storage actions stay deliberate: nothing leaves your backpack just because you walk near a base, and every build or craft still consumes real materials from your inventory or accessible storage.

## TL;DR

- Automatically sort your backpack and opted-in vanilla chests.
- Deposit matching items into nearby storage with one deliberate shortcut.
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
| `Left Alt + E` while targeting a vanilla container | Deposit matching items and replenish protected targets. |
| `Left Alt + E` while a vanilla chest is open | Run the same action using that chest as the target without closing it. |

### Build menu

| Control | Behavior |
| --- | --- |
| `Left Alt` + click a build piece | Withdraw one complete expedition kit for that piece from eligible storage without closing the build menu. |

The modifier follows the configured storage-action shortcut, which uses `Left Alt + E` by default. Version 1.1.8 supports keyboard and mouse; controller-specific controls are not included yet.

## Detailed mechanics

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

> **Test candidate status:** Stackmaster 1.1.8 is a local performance and multiplayer test candidate. Requirement displays share one complete read-only nearby-resource capture for no more than 200 ms. A complete capture validates the storage scope and accessible chest snapshots once; reuse keeps checking exact workbench topology or fallback-radius membership safety, but does not reread access, network state, revisions, ownership, or serialized chest data during that epoch. If the player inventory changes, the next Stackmaster display query rebuilds only the player contribution once and shares the newly combined capture with later queries. Build, craft, upgrade, expedition-kit, ownership, reservation, debit, and rollback paths always capture and validate fresh and cannot use this display cache. Pinned Valheim IL confirms that `Player.OnInventoryChanged` itself uses discovery, `IsKnown`, and `CanAlmostBuild` checks that Stackmaster skips; the pickup path does not directly invoke Stackmaster's `CanBuild` requirement branch. Vanilla still scans inventory/recipes and recreates the placement ghost while a hammer is active, so this candidate implements the requested cache rule but is not yet proven to eliminate the whole pickup hitch. The 1.1.6 player-only fallback is retained and still defers its complete-cost verdict to Valheim itself, including ordinary quality-tier semantics. Stackmaster 1.1.5 remains the current production-ready build until this candidate passes live testing and is separately approved. Live frame-time improvement has not yet been measured; broader multiplayer, dedicated-server, workbench-extension, manual-access-preemption, and load/unload edge-case testing remains incomplete. Back up valuable characters and worlds and report any item loss, duplication, free output, crash, blocked chest, synchronization disagreement, or stale requirement total.

Stackmaster 1.1.8 is compiled from a documented Valheim/Unity/BepInEx/Harmony reference bundle, but exact version strings, file hashes, and assembly MVIDs are provenance and diagnostics—not a runtime allowlist. It supports vanilla containers only. On startup, Stackmaster checks the complete API surface it relies on, including exact overload parameters and every Harmony target, before installing any patch. A missing, changed, or ambiguous contract disables the mod before item-changing hooks are installed; a patching error disables the runtime and removes all Stackmaster patches.

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
