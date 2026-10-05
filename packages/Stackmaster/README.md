# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster by **JStack424** brings inventory sorting, **Quick Stack**, and building and crafting from nearby storage into one Valheim workflow.

> **Stackmaster 1.4.2 test candidate:** Corrects 1.4.1's live startup-contract mismatch for the hover label while preserving its fuel Quick Grab, whole-home crafting-station coverage, ownerless-container recovery, and precise diagnostics. This candidate has passed automated verification but has not yet been live-tested in Valheim.

## About this project

I'm a Valheim-loving software engineer with an interest in mild game design. I built Stackmaster with the assistance of AI as a personal quality-of-life project: smooth out the frustrating bits, preserve the developers' intended experience, and never make progression feel cheesed. I didn't set out to build a widely used mod, but it's been lovely to see people enjoying it. If you've been using Stackmaster and enjoy it, please share any issues or requests through [GitHub Issues](https://github.com/JStack424/Stackmaster/issues). I keep playing, testing, and looking for further improvements, so I'll take thoughtful ideas into account.

## Vanilla-plus by design

Stackmaster removes repetitive container management without adding free resources, carrying capacity, powers, cheats, or progression shortcuts. Quick Stack stays deliberate: nothing leaves your backpack just because you walk near a base, and every build or craft still consumes real materials from your inventory or accessible storage.

## TL;DR

- Automatically sort your backpack and opted-in vanilla chests.
- Use **Quick Stack** to deposit matching items and replenish protected targets with one deliberate shortcut.
- Keep durable per-player, per-world material reservations from valid **Quick Grab Materials** clicks, even away from supplies.
- Build and craft using materials from accessible chests.
- Pull fuel for supported devices with `Left Alt + E`, without loading or operating the device.
- Use one valid crafting station throughout the same connected workbench home when the option is enabled.

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
| `Left Alt + E` while targeting a supported fuel-consuming piece | Pull one prioritized fuel type from eligible chests in the same connected workbench home, up to one full carried stack. The press does not operate or load the piece. |

### Build menu

| Control | Behavior |
| --- | --- |
| `Left Alt` + click a build piece | Add one persistent reservation, then grab one complete material set when safely available, without closing the menu. Pieces whose localized name contains `Stack` or `Pile` are material-only: they still gather the complete set but never create a reservation. |

The modifier follows the configured **Quick Stack** shortcut, which uses `Left Alt + E` by default. While the build menu is open, its keyboard/mouse action list shows **Quick Grab Materials** with **Left Alt + Click** by default and updates that shortcut text when the configured modifiers change. Version 1.2.0 does not add a controller-specific action.

## Detailed mechanics

- **Inventory sorting**
  - Sorts movable backpack slots alphabetically when the inventory opens; the quick bar, equipped items, and protected stacks stay fixed.
  - Adds an **Auto-sort chest** checkbox to supported vanilla chests and remembers each choice locally per player, world, and chest.
  - Sorts enabled chests on open, close, and once after a successful Quick Stack deposit.
  - Keeps protected quantities and reservation-backed materials at the front of the movable region without changing fixed slots.
  - Clears protection only after the whole tracked stack is manually transferred or dropped; partial moves preserve the remainder.
- **One consistent home and storage scope**
  - Inside a workbench build zone, follows overlapping canonical vanilla Workbench zones as one connected home mesh.
  - Includes loaded supported chests anywhere in that exact mesh and excludes nearby chests outside it.
  - Outside every workbench mesh, uses the configurable player-centered radius (20 metres by default) for chest-powered features.
  - Applies the same scope to Quick Stack, building, crafting, Quick Grab Materials, and fuel Quick Grab.
  - Never uses other crafting stations as graph links between separate workbench homes.
- **Quick Stack and protected targets**
  - `Left Alt + E` on a vanilla chest deposits matching stackable items and replenishes protected targets and reserved build materials.
  - Fills matching stacks first, prioritizes the targeted chest, and can reuse a remembered exact-item destination when it is still valid.
  - Keeps unmatched items, overflow, non-stackables, protected quantities, and reserved quantities safely in the backpack.
  - Shows exact deposited, replenished, and left-behind totals, with quantity-specific warnings for eligible items that could not move.
  - Sorts each successfully touched destination at most once, and only when that chest has Auto-sort enabled.
  - Uses safe ownership, revision, capacity, conservation, persistence, and rollback checks before shared inventory changes are accepted.
- **Fuel Quick Grab**
  - `Left Alt + E` on a recognized fuel-consuming piece pulls fuel from eligible chests in the same connected workbench home only.
  - A matching `[Alt + E] Get <fuel>` hint appears below the device's normal interaction text and follows the first available device-priority fuel.
  - Supports refillable fireplaces, smelters and the Charcoal Kiln, fuel-enabled cooking stations, the Shield Generator, and the Battering Ram.
  - Chooses one fuel type per press by device priority, uses the first type with any available stock, and never mixes fuel types.
  - Pulls partial available stock when needed, but never exceeds the selected item's normal maximum stack size or backpack capacity.
  - Does not use fuel already carried by the player as source stock and does not fall back to the outside-home radius.
  - A recognized fuel chord suppresses the same press's normal use/load interaction; ordinary `E`, invalid targets, and container Quick Stack routing remain unchanged.
- **Storage-aware build, craft, and stations**
  - Independently controls chest-backed building, chest-backed crafting, storage-total display, and whole-home crafting-station coverage.
  - Uses player materials first, then the minimum eligible chest set needed for the complete action, without duplicate costs or whole-stack overconsumption.
  - Supports rapid consecutive chest-backed crafts without requiring every other click; each craft owns an isolated reservation and exact cleanup cannot invalidate the next one.
  - Shows exact `required / available` totals for building, crafting, upgrades, quality, and multi-craft quantities when display is enabled.
  - When whole-home stations are enabled, one loaded valid station inside the connected Workbench mesh satisfies that exact station requirement throughout the same mesh.
  - The same exact rule applies to the Hammer station display, building, repairing, and dismantling, so an available same-home station is never shown as `None`.
  - Vanilla in-range station access is always checked first; outside a valid mesh, with the option off, or for mismatched/unloaded stations, vanilla behavior is unchanged.
  - Station coverage changes eligibility only: it does not enlarge station radii, bridge homes, bypass DLC/free-build rules, or provide materials.
- **Quick Grab Materials and reservations**
  - Hold the configured modifier and click a build piece to save one reservation and gather one complete material set when safely available.
  - Repeated clicks add counts even away from supplies; localized `Stack` or `Pile` material pieces gather only and never create reservations.
  - Material transfer is all-or-nothing, capacity-checked, and largest-stock-first; a shortage keeps an ordinary reservation but moves nothing.
  - Reservation cards appear beside the inventory and above the active Hammer menu, select the exact piece, and provide separate one-count release controls.
  - Each genuinely successful paid local placement removes one exact reservation; selection, failure, cancel, free-build, repair, dismantle, and other players do not.
  - Quick Stack retains the exact additive reserved quantities, and orange inventory markers show only the reservation-served share.
- **Safety**
  - Reads requirement totals without claiming ownership and requests ownership only for the exact chests required by an action.
  - Rechecks scope, access, use state, stable identity, ownership, revisions, stack identity, quantities, and persisted inventory before accepting mutation.
  - Rolls back completed steps if a later step fails and verifies global item conservation before crediting the player.
  - Lets a valid readable but temporarily ownerless chest wait up to three seconds for Valheim's exact local assignment, then asks the player to retry the ordinary action; a timeout consumes nothing.
  - Cancels with a specific diagnostic when a required chest is busy, stale, denied, unloaded, out of scope, unreadable, or changed.
  - Releases exact acquired ownership on completion, cancellation, disable, disconnect, logout, unload, shutdown, and exceptions.
  - Disables item-changing behavior if a required Valheim runtime contract is missing, changed, or ambiguous.

## Configuration

Stackmaster has exactly seven settings:

1. **Auto-sort enabled** — on by default and also controlled by the checkbox below the player inventory. Chest auto-sort is controlled separately in each chest UI and is not a seventh global setting.
2. **Nearby-storage radius** — 20 metres by default; configurable from 1 to 50 metres and used by all chest-powered features only while the player is outside every valid connected workbench mesh.
3. **Quick Stack keybind** — Left Alt + E by default. Existing custom shortcuts are migrated automatically from the legacy setting name. Its modifier keys also activate protected-item left/right clicks in the player inventory and Quick Grab Materials left-clicks in the build menu; the main E key is not required for either click action.
4. **Allow building from storage** — on by default and independently controls building eligibility and consumption from storage.
5. **Enable whole-home crafting stations** — on by default; allows an exact required build-gating station anywhere inside the same connected canonical Workbench mesh to cover that home without changing vanilla station radii.
6. **Allow crafting from storage** — on by default and independently controls crafting/upgrade eligibility and consumption from storage.
7. **Show storage amounts in craft and build menus** — on by default; independently shows player-plus-eligible-storage totals in both requirement UIs without granting permission to consume those stored items.

All seven global settings are available through the normal r2modman/BepInEx configuration editor after the first launch. Existing building/crafting opt-outs migrate automatically to the renamed permission settings, so an explicit `false` remains off and the obsolete keys disappear after launch. Per-chest auto-sort choices are local-only preferences scoped to the current player, world, and stable chest identity; a missing or unreadable identity safely skips chest sorting.

## Installation

Install with r2modman, Thunderstore Mod Manager, or another Thunderstore-compatible manager. BepInExPack Valheim is installed as a separate dependency; Stackmaster does not bundle BepInEx or Harmony.

For a manual install, install `denikson-BepInExPack_Valheim` 5.4.2350 or newer, then place `Stackmaster.dll` in `BepInEx/plugins/Stackmaster/`.

## Compatibility and support

> **Stackmaster 1.4.2 test candidate:** Automated tests cover the exact hover-label startup contract, fuel selection, no-mixing and stack caps, connected-home topology, exact station-name matching, ownerless-container preflight and timeout decisions, split diagnostics, and compatibility contracts. Live verification remains required for clean startup, every supported fuel device, input suppression, full-home station coverage, multiplayer/dedicated-server ownership behavior, and load/unload edges. Back up valuable characters and worlds and report any item loss, duplication, free output, unintended device operation, cross-home station access, crash, blocked chest, synchronization disagreement, stale warning, or stale requirement total.

Stackmaster 1.4.2 is compiled from a documented Valheim/Unity/BepInEx/Harmony reference bundle, but exact version strings, file hashes, and assembly MVIDs are provenance and diagnostics—not a runtime allowlist. It supports vanilla containers only. On startup, Stackmaster checks the complete API surface it relies on, including exact overload parameters and every Harmony target, before installing any patch. A missing, changed, or ambiguous contract disables the mod before item-changing hooks are installed; a patching error disables the runtime and removes all Stackmaster patches.

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
