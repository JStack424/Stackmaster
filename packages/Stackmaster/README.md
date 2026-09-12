# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster combines automatic inventory sorting, deliberate nearby-storage depositing, protected-stack replenishment, and exact nearby-chest material use for building and crafting in one Valheim workflow.

## Early public test release

Version 0.2.0 Test Build 6 fixes the Test Build 5 read-only discovery regression that could throw while opening chests or displaying the build/craft UI. Unopened and remote-owned containers now keep live and detached inventory snapshots strictly separate, and nearby-resource availability/HUD patches fail open to the untouched vanilla result if a snapshot cannot be decoded. Accessible vanilla chest stock still remains visible regardless of which peer currently owns it, and read-only HUD snapshots never request ownership. When an action needs a remotely owned chest, Stackmaster safely requests only the minimum chests selected by its player-first plan; because Valheim confirms that request asynchronously, the first attempt prepares ownership without consuming anything and asks you to try the normal action again. Its broader nearby-chest building and crafting paths still need live-game testing. The complete co-op host, co-op guest, and unmodded dedicated-server matrix has not yet been completed, so this build does **not** claim proven multiplayer safety. Back up valuable characters and worlds before early testing, and report any item loss, duplication, crash, corrupt item data, or synchronization disagreement.

Stackmaster is designed as an optional client-side install: each player who wants its features installs it, while the host, other players, and dedicated server should not need Stackmaster. That installation model still needs confirmation across the remaining multiplayer matrix.

## Features

- Sorts movable backpack slots alphabetically whenever the inventory opens.
- Sorts an opened vanilla container at the same time.
- Keeps the whole quick bar, equipped items, and protected stacks fixed.
- Deposits only into nearby eligible vanilla containers that already hold a compatible item.
- Fills partial stacks first, prioritizing the targeted chest and then searching nearest to farthest.
- Replenishes protected stacks to optional target quantities during the same storage action.
- Counts and consumes exact building and crafting costs from the player plus accessible nearby chests, regardless of current network ownership.
- Shows selected build-piece costs as `required / total available` and uses aggregate nearby stock for the shortage blink.
- Shows workbench, forge, cauldron, and equivalent crafting/upgrade costs in the same format, including selected upgrade quality and multi-craft totals.
- Handles quality-specific and multi-craft quantities without consuming whole stacks or charging duplicate costs twice.
- Requests ownership only for chests required by the exact player-first action plan, then rechecks access, ownership, chest use, serialized revision, stack identity, and quantity before removal; failed multi-stack removal rolls back completed steps.
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

Version 0.2.0 supports keyboard and mouse. Controller-specific controls are not included yet.

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

Stackmaster 0.2.0 is built and fail-closed for Valheim API 1.0.12 / Steam build 25253764, Unity 6000.0.75f1, BepInEx runtime 5.4.23.5, and Harmony 2.9.0.0. It supports vanilla containers only. A mismatched or unsafe runtime disables Stackmaster before its inventory hooks can change items. Nearby build/craft totals read stable serialized snapshots from accessible vanilla containers without claiming them, including containers owned by another peer. Mutation remains stricter: after player-first allocation, Stackmaster requests ownership only for the minimum required chests, revalidates their owner/data revisions and exact contents, briefly reserves them for the synchronous transaction, and cancels before mutation if any required chest is busy, denied, stale, or unavailable. Valheim’s owner-authorized request is asynchronous, so a remote-owned action intentionally takes two attempts: the first safely prepares ownership and prompts a retry; only the second, normal vanilla action may consume resources.

Source, issue tracker, and MIT license: <https://github.com/JStack424/Stackmaster>
