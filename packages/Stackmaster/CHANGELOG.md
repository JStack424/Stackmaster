# Changelog

## 0.4.0

Public release.

- Added one uniform storage scope for auto-deposit, protected-stack replenishment, building, and crafting.
- While the player is inside a valid vanilla workbench build zone, Stackmaster now searches the complete connected graph of overlapping loaded workbench zones and treats their exact union as one base mesh.
- Chests inside the connected mesh are eligible throughout the base; nearby chests outside that mesh are excluded.
- While the player is outside every valid workbench mesh, all four chest-powered features use the existing configurable player-centered fallback radius (20 metres by default).
- Preserved targeted-chest-first then nearest-to-player routing, exact minimum-chest build/craft planning, loaded-only discovery, and read-only planning before ownership.
- The per-chest **Auto-sort chest** checkbox still controls sorting only; disabled, manually organized chests remain fully eligible for every chest-powered feature.
- Added one fresh immutable scope snapshot per mutation phase plus topology-aware HUD/resource caching. A connected mesh is never silently truncated by the ordinary nearby-action inspection budget.
- Added fail-closed compatibility checks for the current workbench instances, build-range, prefab-identity, and ZDO APIs used by the mesh resolver.
- Automated verification passes with zero build warnings/errors, 78 pure-domain tests, and 51 static/repository safety checks.
- Core connected-workbench storage behavior has been live-tested in Valheim. Multiplayer behavior, dedicated-server behavior, workbench extensions, and broader load/unload edge cases have not yet been independently verified.

## 0.3.0

- Added an **Auto-sort chest** checkbox beneath supported opened vanilla chests.
- Each chest defaults to enabled and remembers its choice locally per player, world, and stable chest identity. The preference is never written to shared ZDO/world state and cannot affect another player.
- Enabled chests sort on both UI open and UI close, so removing items no longer requires reopening the chest to restore order.
- Disabling one chest takes effect immediately without disabling player-inventory sorting or changing deposit, replenishment, nearby build/craft, or manual organization behavior.
- Chest sorting fails safely when stable identity, local preference storage, ownership, or inventory access is unavailable.
- Automated verification passes with zero build warnings/errors, 64 pure-domain tests, and 50 static/repository safety checks.

## 0.2.2

- Updated the README for clarity and readability. No gameplay changes.

## 0.2.1

Early public testing release.

Version 0.2.1 reissues the approved nearby-resource release under a fresh Thunderstore version number so package managers can distinguish it from an earlier 0.2.0 upload. There are no gameplay changes from the approved candidate.

### Added

- Added independently switchable building and crafting from eligible nearby vanilla chests; both settings default to on and use the existing configurable nearby-storage radius.
- Added exact all-or-nothing material planning across the player inventory and nearby chest stacks, including duplicate-requirement normalization, quality-specific ingredients, and multi-craft quantities.
- Added `required / total available` material rows for selected build pieces and crafting or upgrade menus at workbenches, forges, cauldrons, and equivalent stations.
- Added aggregate shortage coloring: satisfied combined stock stays white, while true shortages retain red flashing.

### Changed

- Material withdrawals now use the player inventory first, then select only the minimum distinct chest set required by the complete plan.
- Display-only scans read stable serialized snapshots from accessible vanilla chests regardless of current network owner and never request ownership.
- Remotely owned actions use Valheim's owner-authorized asynchronous handoff. The first attempt consumes nothing while ownership is prepared and prompts one normal retry; the second attempt performs fresh validation before any withdrawal.
- Ownership prepared by Stackmaster gets a 10-second retry window. Cleanup then returns it to Valheim's native unowned state after success, cancellation, validation failure, rollback, timeout, disable, or disconnect, and retries later rather than releasing ownership it can no longer prove it acquired.

### Safety and fixes

- Revalidates container identity, access, distance, use state, owner revision, serialized data revision, stack identity, quality, world level, and quantity before mutation.
- Briefly reserves only required chests during the synchronous transaction, withdraws exact quantities, and rolls back completed removals if any later step fails.
- Cancels the whole action without partial consumption when any required chest is busy, denied, stale, changed, inaccessible, or out of range.
- Fixed remote ownership outliving build, craft, or Left Alt + E actions and blocking vanilla peers after the modded player disconnected.
- Fixed detached serialized chest items being omitted from totals by hydrating their item metadata from resolved prefabs; any incomplete chest snapshot is rejected as a whole.
- Fixed nearby-resource UI failures interrupting vanilla chest, build, or crafting interfaces; these paths now fail open to vanilla behavior.
- Fixed crafting requirement overlays for Valheim's current static requirement-row method.

### Testing status

- Release build passes with zero warnings and zero errors.
- 61/61 domain tests and 48/48 static/repository safety checks pass.
- Initial live testing passed, including the corrected ownership cleanup behavior.
- The full co-op host, co-op guest, and unmodded dedicated-server matrix remains in progress; this release does not claim proven multiplayer safety.

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
