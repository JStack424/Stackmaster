# Changelog

All notable changes to Stackmaster are documented here.

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
