# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster combines automatic inventory sorting, deliberate nearby-storage depositing, and protected-stack replenishment in one Valheim workflow.

## Early public test release

Version 0.1.0 has passed automated tests and solo smoke testing. The complete co-op host, co-op guest, and unmodded dedicated-server matrix has not yet been completed, so this release does **not** claim proven multiplayer safety. Back up valuable characters and worlds before early testing, and report any item loss, duplication, crash, corrupt item data, or synchronization disagreement.

Stackmaster is designed as an optional client-side install: each player who wants its features installs it, while the host, other players, and dedicated server should not need Stackmaster. That installation model still needs confirmation across the remaining multiplayer matrix.

## Features

- Sorts movable backpack slots alphabetically whenever the inventory opens.
- Sorts an opened vanilla container at the same time.
- Keeps the whole quick bar, equipped items, and protected stacks fixed.
- Deposits only into nearby eligible vanilla containers that already hold a compatible item.
- Fills partial stacks first, prioritizing the targeted chest and then searching nearest to farthest.
- Replenishes protected stacks to optional target quantities during the same storage action.
- Leaves unmatched items and overflow safely in the player inventory.
- Skips inaccessible, unknown, modded, or actively used containers.
- Shows compact totals, shortages, meaningful skips, and incomplete-search notices.
- Disables all item-changing behavior if its runtime compatibility checks fail.

## Controls

- **Open inventory:** Sort movable player slots and any opened vanilla container when Auto-sort is enabled.
- **Left Alt-click an unprotected stackable item:** Set protection and an optional replenishment target. Press Enter to accept the prefilled legal full-stack amount, type a lower legal amount, or enter `0` for protection only.
- **Left Alt-click an unprotected non-stackable item:** Protect it immediately.
- **Left Alt-click a protected item:** Unprotect it.
- **Left Alt + E while targeting a vanilla container:** Deposit matching items and replenish protected targets.
- **Left Alt + E while a vanilla chest is open:** Run the same action using that chest as the target without closing it.

Version 0.1.0 supports keyboard and mouse. Controller-specific controls are not included yet.

## Configuration

Stackmaster has exactly three settings:

1. **Auto-sort enabled** — on by default and also controlled by the checkbox below the player inventory.
2. **Nearby-storage radius** — 20 metres by default; configurable from 1 to 50 metres.
3. **Storage-action keybind** — Left Alt + E by default.

Radius and keybind are available through the normal r2modman/BepInEx configuration editor after the first launch.

## Installation

Install with r2modman, Thunderstore Mod Manager, or another Thunderstore-compatible manager. BepInExPack Valheim is installed as a separate dependency; Stackmaster does not bundle BepInEx or Harmony.

For a manual install, install `denikson-BepInExPack_Valheim` 5.4.2350 or newer, then place `Stackmaster.dll` in `BepInEx/plugins/Stackmaster/`.

## Compatibility and support

Stackmaster 0.1.0 is built and fail-closed for Valheim API 1.0.12 / Steam build 25253764, Unity 6000.0.75f1, BepInEx runtime 5.4.23.5, and Harmony 2.9.0.0. It supports vanilla containers only. A mismatched or unsafe runtime disables Stackmaster before its inventory hooks can change items.

Source, issue tracker, and MIT license: <https://github.com/JStack424/Stackmaster>
