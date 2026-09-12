# Stackmaster

> Turn a messy Viking inventory into a tidy, adventure-ready loadout.

Stackmaster is a Valheim quality-of-life mod by **JStack424** that combines automatic inventory sorting, deliberate nearby-storage depositing, protected-stack replenishment, and exact nearby-chest material use for building and crafting in one tidy workflow.

## Early public test release

Version 0.2.0 Test Build 2 fixes the Test Build 1 startup failure and has passed automated domain and repository checks, but its new nearby-chest building and crafting paths still need live-game testing. The complete co-op host, co-op guest, and unmodded dedicated-server matrix has not yet been completed, so this build does **not** claim proven multiplayer safety. Back up valuable characters and worlds before early testing, and report any item loss, duplication, crash, corrupt item data, or synchronization disagreement.

Stackmaster is designed as an optional client-side install: each player who wants its features installs it, while the host, other players, and dedicated server should not need Stackmaster. That installation model still needs confirmation across the remaining multiplayer matrix.

## Features

- Sorts movable backpack slots alphabetically whenever the inventory opens.
- Sorts an opened vanilla container at the same time.
- Keeps the whole quick bar, equipped items, and protected stacks fixed.
- Lets one protected record follow one compatible stack when it moves or survives a merge.
- Deposits only into nearby eligible vanilla containers that already hold a compatible item.
- Fills compatible partial stacks before creating new stacks.
- Uses the targeted chest first, then searches other matching storage nearest to farthest.
- Replenishes protected stacks to optional target quantities during the same storage action.
- Counts and consumes exact building costs from the player plus eligible nearby chests.
- Counts and consumes exact crafting costs, including quality and multi-craft quantities, from the player plus eligible nearby chests.
- Rechecks access, ownership, chest use, stack identity, and quantity immediately before removal; a failed multi-stack removal rolls back completed steps.
- Leaves unmatched items and overflow safely in the player inventory.
- Skips inaccessible, unknown, modded, or actively used containers.
- Shows compact unit totals, shortages, meaningful skips, and incomplete-search notices.
- Disables all item-changing behavior if its runtime compatibility checks fail.

## Controls

- **Open inventory:** Sort the movable player inventory and any opened vanilla container when Auto-sort is enabled.
- **Left Alt-click an unprotected stackable item:** Choose protection with a replenishment target. The legal full-stack amount is prefilled and selected; press Enter to accept it, type a lower legal amount, or enter `0` for protection only.
- **Left Alt-click an unprotected non-stackable item:** Protect it immediately.
- **Left Alt-click a protected item:** Unprotect it.
- **Left Alt + E while targeting a vanilla container:** Deposit matching items and replenish protected targets.
- **Left Alt + E while a vanilla chest is open:** Run the same action using that chest as the target without closing its inventory.

Version 0.2.0 supports keyboard and mouse. Controller-specific controls are not included yet.

## Configuration

Stackmaster exposes exactly five BepInEx settings:

1. **Auto-sort enabled** — defaults to on and is also available through the checkbox below the player inventory.
2. **Nearby-storage radius** — defaults to 20 metres and can be set from 1 to 50 metres. Deposit, replenishment, building, and crafting all use it.
3. **Storage-action keybind** — defaults to Left Alt + E.
4. **Enable building from nearby chests** — defaults to on; turn it off to restore vanilla player-inventory-only building costs.
5. **Enable crafting from nearby chests** — defaults to on; turn it off to restore vanilla player-inventory-only crafting costs.

All settings are available through the normal r2modman/BepInEx configuration editor after the first launch.

## Installation

### Thunderstore or r2modman

Install **Stackmaster by JStack424** with a Thunderstore-compatible mod manager. Its declared BepInExPack Valheim dependency is installed separately by the manager; Stackmaster does not bundle BepInEx or Harmony.

### Manual

1. Install `denikson-BepInExPack_Valheim` version 5.4.2350 or newer.
2. Copy `Stackmaster.dll` into `BepInEx/plugins/Stackmaster/`.
3. Launch Valheim through the BepInEx-enabled game entry point.

## Compatibility

Stackmaster 0.2.0 is built and fail-closed for the reference environment used during development:

- Valheim API version 1.0.12 / Steam build 25253764
- Unity 6000.0.75f1
- BepInEx runtime 5.4.23.5, supplied by BepInExPack Valheim 5.4.2350
- Harmony 2.9.0.0, supplied by BepInExPack Valheim
- Vanilla containers only

A mismatched or unsafe runtime disables Stackmaster before its inventory hooks can change items. Nearby build/craft stock is deliberately limited to accessible, idle, vanilla containers already owned by the local game peer; a container whose ownership is unresolved is excluded rather than overcounted. Compatibility with other inventory or storage mods is not claimed in 0.2.0.

## Source, issues, and license

- Source and issue tracker: <https://github.com/JStack424/Stackmaster>
- Plugin GUID: `com.jstack424.stackmaster`
- License: [MIT](LICENSE)

## Development

The repository keeps pure inventory policy separate from Valheim/Unity adapters. The deployable plugin targets .NET Framework 4.8, pure planners target .NET Standard 2.0, and automated tests target .NET 8.

Private compile-time references must come from your own Valheim/BepInEx installation and remain under the ignored `lib/local/StackmasterReferences/` directory (or an ignored path override). They are never committed or packaged. On Windows, `scripts/Inspect-StackmasterEnvironment.ps1` performs a read-only environment inventory, and `scripts/Collect-StackmasterReferences.ps1` copies only the required compile-time assemblies from paths you explicitly provide.

```bash
./scripts/build.sh
```

The build restores locked dependencies, builds Release, runs the pure-domain suite, runs repository safety checks, and rejects copied runtime/game assemblies in plugin output. See the [approved behavior contract](docs/DESIGN-QUESTIONNAIRE.md) and [implementation/test plan](docs/NEXT-STEPS.md) for the full design and release gates.
