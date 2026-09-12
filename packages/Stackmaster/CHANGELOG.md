# Changelog

## 0.2.0 Test Build 8

Unpublished local test build.

- Fixed the crafting-row overlay being skipped because Valheim's current `InventoryGui.SetupRequirement` method is static and therefore cannot supply the instance/instance-field arguments requested by the previous postfix.
- Crafting and upgrade rows now show `required / total available` across player and nearby eligible chest stock, remain white when the aggregate satisfies the cost, and flash red only for true shortages.
- Covers workbenches, forges, cauldrons, equivalent stations, upgrades, multi-craft totals, duplicate requirements, and one-ingredient quality constraints.
- Automated verification passes: zero build warnings/errors, 55/55 domain tests, and 45/45 static/repository checks.

## 0.2.0 Test Build 7

Unpublished local test build.

- Fixed the Test Build 6 `/ 0` regression by hydrating detached serialized chest items with shared metadata from their resolved prefabs before resource accounting.
- A detached chest with any unresolved prefab/shared metadata is excluded as a whole instead of contributing a partial snapshot.
- Automated verification passes: zero build warnings/errors, 53/53 domain tests, and 44/44 static/repository checks.

## 0.2.0 Test Build 6

Unpublished local test build.

- Fixed the Test Build 5 null-inventory exception that could interrupt chest opening and the build/craft UI before detached serialized chest data was decoded.
- Read-only discovery now keeps unknown live container inventories ineligible while independently accepting only successfully decoded, accessible detached snapshots.
- Nearby-resource requirement and HUD postfixes now fail open to untouched vanilla results if a snapshot or overlay operation fails; one-ingredient crafting also restores the original result and amount refs.
- Automated verification passes: zero build warnings/errors, 51/51 domain tests, and 43/43 static/repository checks.

## 0.2.0 Test Build 5

Unpublished local test build.

- Accessible vanilla chest stock now appears in build and craft totals regardless of current network owner; display reads stable serialized snapshots and never requests ownership.
- Actions allocate from the player first, then request only the minimum distinct chests used by the complete exact plan; unrelated chests are never claimed.
- Safe remote ownership is asynchronous: the first attempt consumes nothing while it prepares the required ownership and asks for a retry; only the second normal vanilla attempt may perform the action.
- Required ownership, access, radius, use state, owner revision, data revision, stack identity, quality, world level, and quantity are revalidated before removal. Required chests are briefly reserved during the exact synchronous transaction; truly unowned ZDOs, denial, timeout, races, stale snapshots, or changed plans cancel without partial consumption.
- A required chest rejected as busy/unavailable displays exactly `The required materials are currently in use`.
- Exact rollback and vanilla double-charge suppression are retained.
- Automated verification passes: zero build warnings/errors, 48/48 domain tests, and 42/42 static/repository checks.

## 0.2.0 Test Build 4

Unpublished local test build.

- Crafting and upgrade requirement rows at workbenches, forges, cauldrons, and equivalent stations now show `required / total available` across the player inventory and eligible nearby chests.
- Crafting requirement quantities no longer blink red when aggregate nearby stock satisfies the complete cost.
- Display accounting covers duplicate materials, selected upgrade quality, multi-craft quantities, and one-ingredient quality constraints.
- The crafting HUD remains strictly vanilla when nearby-chest crafting or Stackmaster is disabled.
- Consumption logic is unchanged and still performs fresh action-time validation.

## 0.2.0 Test Build 3

Unpublished local test build.

- Selected build-piece requirements now show `required / total available` across the player inventory and eligible nearby chests.
- Requirement quantities no longer blink red when aggregate nearby stock satisfies the complete material requirement.
- The HUD remains strictly vanilla when nearby-chest building or Stackmaster is disabled.
- HUD stock discovery is interval-bounded; placement keeps its existing fresh validation and exact consumption path.

## 0.2.0 Test Build 2

Unpublished local test build.

- Fixed the live-game HarmonyX startup failure caused by binding the recipe requirement hook to the patch's local parameter name instead of Valheim's original parameter position.
- Bound every new nearby-resource hook argument by its method position, preventing harmless game metadata parameter-name differences from disabling Stackmaster at startup.
- Added a regression check covering the positional bindings used by all nearby-resource hooks.

## 0.2.0 Test Build 1

Unpublished local test build.

- Added independently switchable nearby-chest building and crafting; both default to on.
- Reused the existing nearby-storage radius, still 20 metres by default.
- Added exact all-or-nothing cost planning across player inventory and eligible nearby vanilla chests.
- Added duplicate-requirement normalization, quality-aware ingredients, and exact multi-craft totals.
- Added fresh access, in-use, ownership, stack-identity, and quantity validation before withdrawal.
- Added rollback of prior withdrawal steps if a later removal fails.
- Excludes containers with unresolved local ownership rather than overcounting them.
- Automated checks pass; live solo and multiplayer validation of these new paths is still required before publication.

## 0.1.0

Initial public testing release.

- Added automatic alphabetical sorting for movable player-inventory slots and opened vanilla containers.
- Added protected stacks with optional replenishment targets.
- Added one configurable nearby-storage action for matching-item deposits and loadout replenishment.
- Added targeted-container-first, partial-stack-first, nearest-to-farthest routing.
- Added compact visual result feedback and a fail-closed runtime compatibility gate.
- Added exactly three settings: auto-sort, nearby-storage radius, and storage-action keybind.
- Passed automated domain/repository checks and solo smoke testing.
- Co-op host, co-op guest, and unmodded dedicated-server testing remain in progress; this release does not claim proven multiplayer safety.
