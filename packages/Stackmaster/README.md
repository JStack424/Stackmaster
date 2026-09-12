# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster by **JStack424** combines automatic inventory sorting, deliberate nearby-storage depositing, protected-stack replenishment, and exact nearby-chest material use for building and crafting in one Valheim workflow.

## Vanilla-plus by design

Stackmaster is built to preserve the vanilla experience. It adds no gameplay advantage: no free resources, extra carrying capacity, powers, cheats, or progression shortcuts. Instead, it removes repetitive container rummaging so you can spend more time adventuring and building immersive spaces. Put a pantry behind real doors and cook at the nearby station without fetching every ingredient by hand, or unload a mining haul into its organized storage without visiting every chest one by one.

Convenience stays deliberate. Auto-deposit works only when you look at or interact with a chest and press `Left Alt + E`; simply walking near your base never empties your backpack.

> **Tested:** Stackmaster works solo and as a client-only mod connected to a vanilla server. The host, server, and other players do not need Stackmaster. This is still an early public release, so back up valuable characters and worlds and report any item loss, duplication, crash, corrupt item data, blocked chest, or synchronization disagreement.

## Features

- **Inventory sorting**
  - Sorts movable backpack slots alphabetically whenever the inventory opens.
  - Sorts an opened vanilla container at the same time.
  - Keeps the whole quick bar, equipped items, and protected stacks fixed.
- **Auto-deposit**
  - Runs only when you press `Left Alt + E` while targeting or interacting with a vanilla chest.
  - Deposits only into nearby eligible vanilla containers that already hold a compatible item.
  - Fills partial stacks first, prioritizing the targeted chest and then searching nearest to farthest.
  - Leaves unmatched items and overflow safely in the player inventory.
- **Auto-replenish**
  - Refills protected stacks, including ammo and consumables, to optional target quantities during the same storage action.
  - Sets, changes, or removes protection and replenishment targets directly with `Left Alt-click`.
  - Keeps protected stacks fixed while sorting and follows one compatible stack when it moves or survives a merge.
- **Build and craft from nearby chests**
  - Counts and consumes exact building, crafting, and upgrade costs from the player plus accessible nearby vanilla chests within the configured radius, regardless of current network ownership.
  - Shows selected build-piece costs as `required / total available`; satisfied combined stock stays white, while a true shortage flashes red.
  - Shows workbench, forge, cauldron, and equivalent crafting or upgrade costs in the same format, including selected upgrade quality and multi-craft totals.
  - Handles quality-specific and multi-craft quantities without consuming whole stacks or charging duplicate costs twice.
  - Uses the player inventory first, then chooses only the minimum distinct chest set needed by the complete action plan.
- **Safety**
  - Reads nearby build and craft totals without claiming chest ownership.
  - Requests ownership only for required chests, then rechecks access, ownership, chest use, serialized revision, stack identity, and quantity before removal.
  - Withdraws exact quantities and rolls back completed steps if a later removal fails.
  - Cancels before consuming anything and shows `The required materials are currently in use` when a required chest is busy.
  - Returns ownership acquired by Stackmaster after the action or bounded retry window, including cancellation and disconnect cleanup.
  - Never mutates inaccessible, unknown, unsupported, or actively used containers.
  - Shows compact totals, shortages, meaningful skips, and incomplete-search notices.
  - Disables all item-changing behavior if its runtime compatibility checks fail.

## Controls

- **Open inventory:** Sort movable player slots and any opened vanilla container when Auto-sort is enabled.
- **Left Alt-click an unprotected stackable item:** Set protection and an optional replenishment target. Press Enter to accept the prefilled legal full-stack amount, type a lower legal amount, or enter `0` for protection only.
- **Left Alt-click an unprotected non-stackable item:** Protect it immediately.
- **Left Alt-click a protected item:** Unprotect it.
- **Left Alt + E while targeting a vanilla container:** Deposit matching items and replenish protected targets.
- **Left Alt + E while a vanilla chest is open:** Run the same action using that chest as the target without closing it.

Version 0.2.2 supports keyboard and mouse. Controller-specific controls are not included yet.

## Configuration

Stackmaster has exactly five settings:

1. **Auto-sort enabled** — on by default and also controlled by the checkbox below the player inventory.
2. **Nearby-storage radius** — 20 metres by default; configurable from 1 to 50 metres and shared by all nearby-storage features.
3. **Storage-action keybind** — Left Alt + E by default.
4. **Enable building from nearby chests** — on by default and independently switchable.
5. **Enable crafting from nearby chests** — on by default and independently switchable.

All settings are available through the normal r2modman/BepInEx configuration editor after the first launch.

## Installation

Install with r2modman, Thunderstore Mod Manager, or another Thunderstore-compatible manager. BepInExPack Valheim is installed as a separate dependency; Stackmaster does not bundle BepInEx or Harmony.

For a manual install, install `denikson-BepInExPack_Valheim` 5.4.2350 or newer, then place `Stackmaster.dll` in `BepInEx/plugins/Stackmaster/`.

## Compatibility and support

Stackmaster 0.2.2 is built and fail-closed for Valheim API 1.0.12 / Steam build 25253764, Unity 6000.0.75f1, BepInEx runtime 5.4.23.5, and Harmony 2.9.0.0. It supports vanilla containers only. A mismatched or unsafe runtime disables Stackmaster before its inventory hooks can change items.

Nearby build/craft totals read stable serialized snapshots from accessible vanilla containers without claiming them, including containers owned by another peer. Mutation remains stricter: after player-first allocation, Stackmaster requests ownership only for the minimum required chests, revalidates their owner/data revisions and exact contents, briefly reserves them for the synchronous transaction, and cancels before mutation if any required chest is busy, denied, stale, or unavailable. Valheim’s owner-authorized request is asynchronous, so a remote-owned action intentionally takes two attempts: the first prepares ownership and prompts a retry; only the second, normal vanilla action may consume resources. The retry window lasts 10 seconds. Cleanup then returns only ownership demonstrably acquired by Stackmaster to Valheim's unowned state, retrying later if an immediate release cannot be proven safe.

Plugin GUID: `com.jstack424.stackmaster`

Source, issue tracker, and MIT license: <https://github.com/JStack424/Stackmaster>

## Development

The repository keeps pure inventory policy separate from Valheim/Unity adapters. The deployable plugin targets .NET Framework 4.8, pure planners target .NET Standard 2.0, and automated tests target .NET 8.

Private compile-time references must come from your own Valheim/BepInEx installation and remain under the ignored `lib/local/StackmasterReferences/` directory (or an ignored path override). They are never committed or packaged. On Windows, `scripts/Inspect-StackmasterEnvironment.ps1` performs a read-only environment inventory, and `scripts/Collect-StackmasterReferences.ps1` copies only the required compile-time assemblies from paths you explicitly provide.

```bash
./scripts/build.sh
```

The build restores locked dependencies, builds Release, runs the pure-domain suite, runs repository safety checks, and rejects copied runtime/game assemblies in plugin output. See the [approved behavior contract](docs/DESIGN-QUESTIONNAIRE.md) and [implementation/test plan](docs/NEXT-STEPS.md) for the full design and release gates.
