# Changelog

## 0.4.2

Local r2modman test candidate; not published.

- Split nearby-resource behavior into three independent, default-on settings: **Allow crafting from storage**, **Allow building from storage**, and **Show storage amounts in craft and build menus**.
- Storage totals can now remain visible as exact `required / total available` values in crafting, upgrade, and building rows even when the corresponding action is restricted to player-held materials.
- Requirement affordability and red/white flashing now follow actual action permission on both code paths: player-only stock when that action's storage permission is off, aggregate player-plus-eligible-storage stock when it is on.
- Turning storage totals off preserves vanilla count text and typography while storage-backed crafting or building can remain enabled independently; turning both display and the relevant permission off leaves that requirement UI untouched.
- Preserved the connected-workbench mesh / fallback scope, exact aggregate accounting, adaptive long requirement-count fit, independent crafting/building consumption paths, and all 0.4.1 ownership and protected-item cleanup behavior.
- Automated tests cover all eight combinations of the three new booleans across both crafting and building UI decisions. Live in-game validation remains pending.

## 0.4.1

Local r2modman test candidate; not published.

- Added a 30-second sliding Valheim ownership lease after a successful chest-backed building placement. Only exact chests Stackmaster demonstrably acquired and actually used are retained, and each successful build use renews that chest's lease.
- Logical `in-use` reservations still end immediately after the atomic transaction; crafting behavior is unchanged and does not retain the new build lease.
- A remote player's manual chest-open request immediately invalidates an idle retained build lease and continues through Valheim's normal request and ownership-transfer path. The local owner's own manual access remains available and does not invalidate the lease.
- Lease expiry returns the exact tracked chest to Valheim owner `0` only while identity, local session, current owner, and successor owner-revision guards still prove it is the ownership Stackmaster acquired.
- Cancellation, failure, rollback, partial or unused acquisition, disable, disconnect, logout, scene unload, shutdown, hot unload, compatibility disable, and exceptions preserve immediate guarded cleanup. Unrelated, merely inspected, already-local, or non-acquired chests are never retained or released.
- Preserved exact nearby-resource accounting, minimum mutated-chest planning, connected-workbench/20 m fallback scope, chest auto-sort opt-outs, and the exact busy message `The required materials are currently in use`.
- Crafting and upgrade requirement labels now keep every digit visible by using the normal vanilla font size when it fits and bounded TextMeshPro auto-sizing for longer exact totals such as `45 / 172` and `999 / 999`; pooled rows and disabled/fail-open paths restore their original typography.
- Manually transferring a whole protected/targeted stack into a chest or dropping it into the world now clears that stack's protection immediately. Partial external moves retain the protected remainder, while moves and merges wholly inside the player inventory keep existing item-following behavior.
- Storage hotkey planning now prunes older orphaned target records that cannot resolve any compatible player-inventory item, preventing stale `target item missing` shortage notices without disturbing valid moved targets.
- Automated verification passes with zero build warnings/errors, 95 pure-domain tests, and 55 static/repository safety checks.
- The ownership-lease change has not yet been independently tested in multiplayer, on dedicated servers, with workbench extensions, or across broader load/unload topologies.

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
