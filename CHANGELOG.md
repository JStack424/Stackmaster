# Changelog

All notable changes to Stackmaster are documented here.

## [0.2.0 Test Build 7] - 2026-09-12

Unpublished local test build.

### Fixed

- Fixed the Test Build 6 `/ 0` regression: detached serialized chest items now hydrate their shared item metadata from each resolved prefab before nearby-resource accounting.
- If any detached item lacks a resolved prefab or complete shared metadata, the entire affected chest is excluded rather than exposing a partial or misleading resource snapshot.

### Automated verification

- Release build succeeds with zero warnings and zero errors.
- 53/53 pure-domain tests and 44/44 static/repository checks pass.

## [0.2.0 Test Build 6] - 2026-09-12

Unpublished local test build.

### Fixed

- Fixed the Test Build 5 null-inventory exception that could interrupt chest opening and the build/craft UI before detached serialized chest data was decoded.
- Read-only discovery now keeps unknown live container inventories ineligible while independently accepting only successfully decoded, accessible detached snapshots.
- Nearby-resource requirement and HUD postfixes now fail open to untouched vanilla results if a snapshot or overlay operation fails; one-ingredient crafting also restores the original result and amount refs.

### Automated verification

- Release build succeeds with zero warnings and zero errors.
- 51/51 pure-domain tests and 43/43 static/repository checks pass.

## [0.2.0 Test Build 5] - 2026-09-12

Unpublished local test build.

### Fixed

- Building and crafting totals now include stable serialized stock from accessible vanilla chests regardless of current network owner; displaying totals never requests or claims ownership.
- Complete actions remain player-first and select only the minimum distinct chests used by the exact all-or-nothing withdrawal plan; unrelated chests are never requested.
- Remote ownership uses Valheim's owner-authorized asynchronous request rather than blind `ClaimOwnership`. The original action is canceled without mutation while ownership is prepared, then the player is asked to retry through the complete vanilla action path.
- Required chest identity, access, radius, in-use state, owner revision, serialized data revision, stack identity, quality, world level, and quantity are revalidated before mutation. Required chests are briefly reserved during the exact synchronous transaction; truly unowned ZDOs, plan changes, denial, timeout, stale data, or any failed check cancel without partial consumption.
- Owner rejection for a required busy/unavailable chest displays exactly `The required materials are currently in use`.
- Exact rollback and vanilla double-charge suppression remain in place.

### Verified limitation

- Valheim's safe owner-authorized ownership RPC completes asynchronously. A Harmony action prefix cannot synchronously wait for it, and replaying placement later would bypass vanilla stamina, durability, statistics, and other logic. Test Build 5 therefore deliberately requires a second normal build/craft attempt after ownership is prepared instead of forcing or replaying the action.

### Automated verification

- Release build succeeds with zero warnings and zero errors.
- 48/48 pure-domain tests and 42/42 static/repository checks pass.

## [0.2.0 Test Build 4] - 2026-09-12

Unpublished local test build.

### Added

- Workbench, forge, cauldron, and equivalent crafting/upgrade requirement rows now show `required / total available` across the player inventory and eligible nearby chests.

### Fixed

- Crafting requirement quantities no longer blink red when aggregate nearby stock satisfies the complete cost.
- Crafting HUD accounting now applies the selected upgrade quality and multi-craft multiplier, normalizes duplicate requirements, and preserves one-ingredient quality constraints.
- The crafting HUD override is gated by the crafting-from-nearby-chests setting and Stackmaster compatibility state, preserving vanilla UI when disabled.
- HUD stock discovery is refreshed at a bounded interval; final crafting validation and consumption remain unchanged and freshly revalidated at action time.

## [0.2.0 Test Build 3] - 2026-09-12

Unpublished local test build.

### Added

- Selected build-piece requirements now show `required / total available`, where available stock includes the player inventory plus the same eligible nearby chests used for building.

### Fixed

- Requirement quantities no longer blink red when aggregate nearby stock satisfies the full material requirement, including normalized duplicate-material costs.
- The HUD override is gated by the building-from-nearby-chests setting and Stackmaster compatibility state, preserving vanilla UI when disabled.
- HUD stock discovery is refreshed at a bounded interval while the red/white blink remains smooth each frame; placement still performs the existing fresh action-time validation before consuming anything.

## [0.2.0 Test Build 2] - 2026-09-12

Unpublished local test build.

### Fixed

- Fixed the live-game HarmonyX startup failure caused by binding the recipe requirement hook to the patch's local parameter name instead of Valheim's original parameter position.
- Bound every new nearby-resource hook argument by its method position, preventing harmless game metadata parameter-name differences from disabling Stackmaster at startup.
- Added a regression check covering the positional bindings used by all nearby-resource hooks.

## [0.2.0 Test Build 1] - 2026-09-11

Unpublished local test build.

### Added

- Optional building from eligible nearby vanilla chests, enabled by default.
- Optional crafting from eligible nearby vanilla chests, enabled by default and independently switchable.
- Shared use of the existing configurable nearby-storage radius, which remains 20 metres by default.
- Exact all-or-nothing material planning across player and chest stacks, with duplicate requirement normalization, quality filtering, and multi-craft quantity support.
- Fresh action-time validation of chest access, idle state, ownership, stack identity, and quantity.
- Verified rollback for completed withdrawal steps if a later stack removal fails.

### Safety and testing status

- A 50-unit cost with only 25 available is rejected without any withdrawal plan.
- A 50-unit cost spread across partial stacks plans exactly 50 units; whole stacks are never removed unnecessarily.
- Containers with unresolved local ownership are excluded rather than counted optimistically.
- Automated domain and repository checks pass; live solo and multiplayer checks for the new paths remain required before publication.

## [0.1.0] - 2026-09-11

Initial public testing release.

### Added

- Automatic alphabetical sorting for movable player-inventory slots and opened vanilla containers.
- Protected stacks that stay fixed and follow one matching item stack when it moves or survives a merge.
- Optional per-stack replenishment targets, configured with Left Alt-click.
- One configurable storage action that deposits matching items and replenishes protected targets across nearby eligible vanilla containers.
- Targeted-container-first routing, partial-stack-first filling, and nearest-to-farthest fallback routing.
- Compact visual summaries for deposited, replenished, left-behind, shortage, skipped-container, and incomplete-search results.
- A fail-closed runtime compatibility gate for the validated Valheim and dependency versions.
- Three configuration settings: auto-sort, nearby-storage radius, and storage-action keybind.

### Testing status

- Automated domain and repository safety suites pass.
- Solo smoke testing has passed for the current gameplay and UI behavior.
- Co-op host, co-op guest, and unmodded dedicated-server testing is still in progress; this release does not claim proven multiplayer safety.
