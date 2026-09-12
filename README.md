# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster by **JStack424** combines automatic inventory sorting, deliberate nearby-storage depositing, protected-stack replenishment, and exact nearby-chest material use for building and crafting in one Valheim workflow.

## Early public testing release

Version 0.2.1 adds building and crafting from accessible nearby vanilla chests, including chests currently owned by another peer. Build-piece and crafting requirement rows show `required / total available` across the player inventory and eligible nearby storage, staying white when the combined stock is sufficient and flashing red only for a true shortage.

Stackmaster reads these totals without claiming ownership. When an action needs a remotely owned chest, the first attempt safely prepares ownership without consuming anything and asks you to retry. The retry opportunity lasts for a bounded 10-second window. Cleanup then returns ownership to Valheim's native unowned state after success, cancellation, validation failure, rollback, timeout, disable, or disconnect; if immediate release cannot be proven safe, cleanup keeps retrying rather than touching uncertain ownership. Only the minimum required chest set is requested, and the complete action is revalidated before exact withdrawal.

Stackmaster is designed as an optional client-side install: each player who wants its features installs it, while the host, other players, and dedicated server should not need Stackmaster. Initial live testing has passed, but the full co-op host, co-op guest, and unmodded dedicated-server matrix remains incomplete. This release does **not** claim proven multiplayer safety. Back up valuable characters and worlds before early testing, and report any item loss, duplication, crash, corrupt item data, blocked chest, or synchronization disagreement.

## Features

- Sorts movable backpack slots alphabetically whenever the inventory opens.
- Sorts an opened vanilla container at the same time.
- Keeps the whole quick bar, equipped items, and protected stacks fixed.
- Lets each protection record follow one compatible stack when it moves or survives a merge.
- Deposits only into nearby eligible vanilla containers that already hold a compatible item.
- Fills partial stacks first, prioritizing the targeted chest and then searching nearest to farthest.
- Replenishes protected stacks to optional target quantities during the same storage action.
- Counts and consumes exact building and crafting costs from the player plus accessible nearby chests, regardless of current network ownership.
- Shows selected build-piece costs as `required / total available` and uses aggregate nearby stock for the shortage blink.
- Shows workbench, forge, cauldron, and equivalent crafting/upgrade costs in the same format, including selected upgrade quality and multi-craft totals.
- Handles quality-specific and multi-craft quantities without consuming whole stacks or charging duplicate costs twice.
- Uses the player inventory first, then chooses only the minimum distinct chest set needed by the complete action plan.
- Requests ownership only for required chests, then rechecks access, ownership, chest use, serialized revision, stack identity, and quantity before removal.
- Withdraws exact quantities and rolls back completed steps if a later removal fails.
- Returns ownership acquired by Stackmaster after the action or bounded retry window, including cancellation and disconnect cleanup.
- Leaves unmatched items and overflow safely in the player inventory.
- Never mutates inaccessible, unknown, modded, or actively used containers.
- Shows compact totals, shortages, meaningful skips, and incomplete-search notices.
- Disables all item-changing behavior if its runtime compatibility checks fail.

## Controls

- **Open inventory:** Sort movable player slots and any opened vanilla container when Auto-sort is enabled.
- **Left Alt-click an unprotected stackable item:** Set protection and an optional replenishment target. Press Enter to accept the prefilled legal full-stack amount, type a lower legal amount, or enter `0` for protection only.
- **Left Alt-click an unprotected non-stackable item:** Protect it immediately.
- **Left Alt-click a protected item:** Unprotect it.
- **Left Alt + E while targeting a vanilla container:** Deposit matching items and replenish protected targets.
- **Left Alt + E while a vanilla chest is open:** Run the same action using that chest as the target without closing it.

Version 0.2.1 supports keyboard and mouse. Controller-specific controls are not included yet.

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

Stackmaster 0.2.1 is built and fail-closed for Valheim API 1.0.12 / Steam build 25253764, Unity 6000.0.75f1, BepInEx runtime 5.4.23.5, and Harmony 2.9.0.0. It supports vanilla containers only. A mismatched or unsafe runtime disables Stackmaster before its inventory hooks can change items.

Nearby build/craft totals read stable serialized snapshots from accessible vanilla containers without claiming them, including containers owned by another peer. Mutation remains stricter: after player-first allocation, Stackmaster requests ownership only for the minimum required chests, revalidates their owner/data revisions and exact contents, briefly reserves them for the synchronous transaction, and cancels before mutation if any required chest is busy, denied, stale, or unavailable. Valheim’s owner-authorized request is asynchronous, so a remote-owned action intentionally takes two attempts: the first prepares ownership and prompts a retry; only the second, normal vanilla action may consume resources. The retry window lasts 10 seconds. Cleanup then returns only ownership demonstrably acquired by Stackmaster to Valheim's unowned state, retrying later if an immediate release cannot be proven safe. Compatibility with other inventory or storage mods is not claimed in 0.2.1.

Plugin GUID: `com.jstack424.stackmaster`

Source, issue tracker, and MIT license: <https://github.com/JStack424/Stackmaster>

## Development

The repository keeps pure inventory policy separate from Valheim/Unity adapters. The deployable plugin targets .NET Framework 4.8, pure planners target .NET Standard 2.0, and automated tests target .NET 8.

Private compile-time references must come from your own Valheim/BepInEx installation and remain under the ignored `lib/local/StackmasterReferences/` directory (or an ignored path override). They are never committed or packaged. On Windows, `scripts/Inspect-StackmasterEnvironment.ps1` performs a read-only environment inventory, and `scripts/Collect-StackmasterReferences.ps1` copies only the required compile-time assemblies from paths you explicitly provide.

```bash
./scripts/build.sh
```

The build restores locked dependencies, builds Release, runs the pure-domain suite, runs repository safety checks, and rejects copied runtime/game assemblies in plugin output. See the [approved behavior contract](docs/DESIGN-QUESTIONNAIRE.md) and [implementation/test plan](docs/NEXT-STEPS.md) for the full design and release gates.
