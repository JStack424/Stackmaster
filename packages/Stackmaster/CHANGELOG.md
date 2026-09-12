# Changelog

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
