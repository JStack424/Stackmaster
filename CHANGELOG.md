# Changelog

All notable changes to Stackmaster are documented here.

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
