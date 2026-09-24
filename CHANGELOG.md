# Changelog

## 1.1.8 (local test candidate)

Reduced Stackmaster's continuous hammer-mode requirement-display workload and added an in-menu Quick Grab Materials hint without weakening action-time validation.

- Added a keyboard/mouse build-menu hint beside Valheim's existing bottom-of-screen actions: **Quick Grab Materials** with **Left Alt + Click** by default. It follows the configured storage-action modifiers, avoids duplicate entries, preserves other mods' hints, removes only Stackmaster's hint during cleanup/reload, and intentionally adds no controller binding.
- Build and crafting display snapshots now pre-index item and quality totals once. Repeated `CanBuild` checks use constant-time lookups instead of sorting and running the exact withdrawal planner over every player and chest stack for each icon; action-time checks still use the exact planner.
- Stable display callers reuse the completed snapshot and scope for a one-second display window instead of repeating workbench enumeration, container discovery, detached inventory decoding, and aggregation. Player inventory changes rebuild only the player contribution and may retain the already-validated chest slice past that ordinary window when scope topology and membership remain safe.
- The chest timestamp is separate and never renewed by player-only refreshes. Repeated pickups therefore cannot keep storage data alive indefinitely: the chest slice reaches a five-second absolute bound and is then rediscovered.
- Opening an already-sorted inventory no longer emits a synthetic `Inventory.m_onChanged` notification. With a hammer equipped, that avoids an unnecessary vanilla placement-ghost rebuild when Stackmaster moved nothing.
- Corrected the pinned Valheim call-path proof: `Player.OnInventoryChanged` updates known recipes and available pieces, then recreates the placement ghost. Its recipe check uses discovery mode, and its piece checks use `IsKnown` or `CanAlmostBuild`; Stackmaster intentionally skips all three. The pickup path itself does not directly call Stackmaster's `CanBuild` branch. The Stackmaster-only load is the normal hammer HUD loop around that vanilla rebuild: requirement display checks repeatedly resolved storage scope and periodically performed full chest discovery.
- Expiration, workbench structural-topology changes, and fallback-radius membership-boundary movement still force a complete fresh display capture. Every build, craft, upgrade, quick-grab, ownership, reservation, debit, rollback, and cleanup path remains fresh, synchronous, and unable to consume display cache state.
- Added pinned `KeyHints.Update`/keyboard-hint contracts, exact IL checks for Valheim's inventory/build call paths, indexed-vs-raw availability parity tests, bounded chest-age tests, player-only refresh tests, and repository checks for hint formatting, keyboard-only insertion, duplicate avoidance, and cleanup.
- This candidate targets the proven Stackmaster display overhead but is not yet a live-proven complete fix for the reported frame drops. Vanilla still scans inventory/recipes and, while a hammer is active, destroys and recreates the placement ghost after a real inventory change. Live comparison against 1.1.7 and 1.1.5 is required before productionization.

## 1.1.7 (local test candidate)

Bounded display-snapshot reuse to reduce hammer and crafting-menu frame drops without weakening action-time correctness.

- Build-grid, selected-piece HUD, and crafting/upgrade requirement displays now share one complete read-only nearby-resource capture for at most 200 ms instead of repeating scene-wide container discovery, ZDO inventory decode, detached `Inventory.Load`, metadata hydration, and stack aggregation every rendered frame. In a stable open menu this bounds complete display captures to about five per second; live in-game frame-time improvement is not yet measured.
- A display epoch is published only after full-scope discovery completes. Every build, craft, upgrade, quick-grab, ownership, reservation, planning, debit, and rollback path still performs its own fresh synchronous capture and cannot read or populate the display cache.
- Fallback-radius reuse is allowed only while player movement remains strictly inside the shortest proven distance to any container membership boundary. Reaching that boundary, changing the radius, changing workbench topology, or changing scope kind forces full rediscovery.
- Before every reuse, each cached chest must still match its exact container/network identity, active scope membership, access result, ZDO identity, data revision, owner revision, owner, and serialized inventory payload byte-for-byte. A chest change therefore invalidates detached decode and stack-summary reuse.
- Player inventory is fingerprinted independently. Pickups, consumption, drops, movement between slots, quality changes, and world-level changes rebuild the player portion immediately while retaining only already-validated chest summaries.
- Tightened the 1.1.6 outside-workbench player-only crafting bypass to ask Valheim's own `HaveRequirementItems` implementation for the live complete cost, preventing Stackmaster from entering the bypass on any ordinary ingredient quality combination vanilla would reject. Chest-needed and workbench-mesh crafts remain guarded.
- Added deterministic policy and source-contract regressions for bounded age, cross-caller sharing, movement boundaries, workbench topology, exact payload invalidation, player inventory refresh, forced-fresh action isolation, guarded chest/workbench crafts, the 1.1.6 multiplayer fallback path, and quality-tier safety.
- This candidate still requires live performance and multiplayer validation before productionization.

## 1.1.6 (local test candidate)

Narrow player-inventory crafting bypass outside workbench coverage.

- When the player is outside every connected workbench mesh and the live player inventory alone satisfies the complete current recipe cost, Stackmaster now leaves validation, ingredient selection, and debit entirely to vanilla instead of creating a nearby-storage preflight.
- The player-only decision is recomputed at both craft start and completion and honors upgrades, multi-craft quantities, world-level requirements, duplicate requirements, and one-of ingredient recipes without combining incompatible quality tiers.
- Because this path creates no Stackmaster intent, scope signature, reservation, ownership request, prepared transaction, or removal suppression, nearby-player movement and unrelated shared-storage or workbench-topology churn cannot cancel a craft that never needed chest materials.
- Crafts that need even one chest item, all workbench-mesh crafts, and unresolved scopes remain on the existing guarded Stackmaster path with exact planning, ownership, reservations, revision and identity checks, debit journaling, rollback, and cleanup unchanged.
- This candidate still requires live multiplayer validation before productionization.

## 1.1.5

Public release of the live-tested, fail-closed repair for chest-backed crafting transactions.

- Fixed remote crafting so prepared withdrawals are recaptured from each selected owned `Container.GetInventory()` rather than from detached ZDO inventory snapshots used only for read-only discovery and planning.
- The prepared transaction now requires the live owned-inventory plan to match the original exact plan, reserves and revalidates only selected chests, and rejects any detached inventory identity before removal.
- Centralized the exact withdrawal journal: every observed player or chest mutation is recorded once, exact quantities are required, incomplete debits cannot commit, and cancellation or failure rolls receipts back in reverse order.
- Preserved vanilla charge suppression after Stackmaster's exact debit, so successful remote crafts pay once—not zero times or twice. Storage-disabled and NoCost modes remain vanilla.
- Added regressions for mixed player/chest debit, selected-chest-only mutation, duplicate-charge prevention, incomplete-debit rejection, and exact cancellation/failure rollback.
- Strengthened the pinned Valheim release gate to verify that successful-output crafting still reaches the exact string-based `Inventory.RemoveItem` overload and `Player.ConsumeResources`, after the output-add call shape Stackmaster's transaction depends on.
- Live-tested the corrected chest-backed crafting debit in Valheim before promoting this exact gameplay candidate to production.

## 1.1.4

Harmony-target manifest correction and release-gate hardening with no gameplay or configuration changes. This build supersedes 1.1.3.

- Fixed startup patch preflight for `InventoryGui.SetupRequirement(Transform, Requirement, Player, bool, int, int)`: the compatibility gate correctly validated this Valheim API as `static`, but the separate patch installer incorrectly searched for an instance method.
- Replaced every duplicated target declaration, including target-bearing patch attributes and the hot-unload cleanup resolver, with one canonical Harmony target manifest consumed by compatibility validation and every installation path. Each descriptor pins the declaring type, exact overload, static/instance shape, return type, patch entrypoints, and whether it belongs to the cleanup-safety subset.
- Added fail-closed Harmony patch-signature validation for original arguments, `__instance`, `__result`, `__state`, `__exception`, and injected fields before any patch is installed.
- Added the release-gating `HarmonyTargetManifestReleaseGate`, which resolves all 32 patch operations against the pinned Valheim assemblies and proves `PatchInstaller.Prepare()` returns those exact resolved `MethodInfo` objects. Packaging now runs the complete build/test gate itself and refuses to create a ZIP if this check fails.
- Made release binaries byte-reproducible across clean checkout paths by enabling deterministic source paths and requiring the generated Source Link map to use the normalized `/_/` root and pinned release revision.
- Runtime identity, version, SHA-256, and MVID remain diagnostic-only. All Stackmaster 1.1.0 gameplay and settings remain unchanged, including the rule that modifier + right-click never clears protection.

## 1.1.3

Compatibility-gate static-method correction with no gameplay or configuration changes. This build supersedes 1.1.2.

- Corrected four contract declarations that accidentally requested instance methods even though Stackmaster and Valheim expose the APIs statically: `ZDOMan.GetSessionID()`, `GameCamera.InFreeFly()`, `ZInput.ResetButtonStatus(string)`, and `PrivateArea.CheckAccess(Vector3, float, bool, bool)`.
- Preserves fail-closed validation for all four APIs. Session identity remains mandatory for ownership and reservation safety; free-fly detection and input reset remain mandatory input guards; ward access remains mandatory before guarded containers can be used.
- Added regressions for each missing API and instance-shaped lookalike, plus pinned metadata checks for the exact static signatures in both `assembly_valheim.dll` and `assembly_utils.dll`.
- Runtime identity, version, SHA-256, and MVID remain diagnostic-only. All Stackmaster 1.1.0 gameplay and settings remain unchanged.

## 1.1.2

Compatibility-gate correction with no gameplay or configuration changes.

- Replaced exact Valheim label, Unity/BepInEx/Harmony version, `assembly_valheim` SHA-256, and MVID runtime enforcement with a contract-based fail-closed gate.
- Runtime identity values remain diagnostics only; the pinned private-reference hashes remain build provenance and are not a client allowlist.
- Validates every declared dependency in the runtime contract—required types, exact method overloads and parameters, constructors, fields, properties, static/instance shape, Harmony targets, and patch entrypoints—before installing the first patch.
- A missing or ambiguous contract disables Stackmaster before patching. Any Harmony installation failure fully disables the runtime and removes all patches installed under Stackmaster's Harmony ID.
- Added focused executable/static tests proving differing identity metadata is accepted when contracts match, exact-version constants are absent from the runtime gate, required contracts are covered, and missing or ambiguous members fail closed.
- Preserves the 1.1.0 gameplay, settings, networking, ownership, reservation, and persistence behavior exactly.

## 1.1.1

Release package prepared with the exact DLL bytes supplied for live validation.

- Revalidated the unchanged 1.1.0 gameplay and configuration against Valheim 1.0.14, anonymous Steam dedicated-server build 25364309, and Unity 6000.0.75f1.
- Updated the fail-closed Valheim assembly fingerprint and private reference bundle.
- Added deterministic private-reference fingerprint validation plus 84 independent metadata/IL compatibility checks, including exact left-click/right-click inventory routing.
- No gameplay behavior, settings, networking, ownership, reservation, or persistence semantics changed.

## 1.1.0

Public release, preserving the live-validated 1.1.0 behavior without gameplay or configuration changes.

- Replaced the former all-in-one protected-item modifier-click prompt with separate mouse controls using the configured storage-action modifier (Left Alt by default).
- Modifier + left-click now toggles protection immediately with no dialog: an unprotected item becomes protection-only, while any protected item is fully unprotected and loses its target in one click.
- Modifier + right-click now opens the restocking quantity dialog for stackable items. Confirming protects the item and adds or edits its target; canceling or entering an invalid quantity preserves the prior state exactly.
- Non-stackable items remain protection-only: modifier + right-click is consumed without changing their state. All unmodified inventory clicks remain vanilla.
- Added **Quick Grab Materials** to the active build-piece menu: hold the configured storage-action modifier (Left Alt by default) and click a piece to grab all materials for one complete copy from eligible storage while keeping the menu open.
- Every modified click requests a fresh complete material set and intentionally ignores materials already carried by the player.
- For each ingredient, withdraws from the eligible chest holding the largest total stock first, then smaller sources, with deterministic tie-breaking.
- Preflights the complete material set against exact inventory slots and added carry weight before requesting any chest ownership.
- Replans from fresh storage after asynchronous ownership acquisition, requires an identical plan, reserves and revalidates every required chest, then performs exact source-to-destination moves.
- Cancels without moving anything on shortage, access or ownership changes, busy storage, stale contents, insufficient slots, or insufficient carry capacity. Late transfer failures roll back completed moves before guarded ownership cleanup.
- Emergency snapshot rollback restores the original player item objects and their complete captured state, preserving every equipped-item reference; chest snapshots remain detached.
- A failed exact local in-use cleanup is retained and retried before Stackmaster may relinquish its matching ownership acquisition, including disable and hot-unload paths, without clearing later or unrelated owners' state.
- Logging out now performs a recoverable session teardown rather than permanently disabling Stackmaster for the remaining Valheim process. Joining a new server session rearms every feature without requiring a game restart.
- Reconnect rearming is fail-closed: rollback and reservation-before-ownership cleanup must complete successfully, the network-session object must change, permanent compatibility failures stay disabled, and stale coroutines are generation-isolated from later sessions.
- Chest-backed crafting is now one click: Stackmaster captures the exact recipe/upgrade/variant/multi-craft intent, acquires only the minimum required chest set before starting vanilla crafting, marks every selected chest in use, and resumes that original intent automatically.
- Ownership waiting no longer starts a disposable progress bar. The normal vanilla craft duration and animation begin exactly once, only after every required chest is reserved, and the atomic withdrawal occurs only when vanilla reaches `DoCrafting`.
- Craft cancel, recipe or tab changes, inventory close, scope/station changes, failed validation, exceptions, timeout, disable, disconnect, and shutdown immediately clear reservations and release only exact proven Stackmaster acquisitions; delayed callbacks are generation- and session-isolated.
- Uses the existing configured shortcut modifier, connected-workbench/fallback storage scope, vanilla ownership handshake, and access/in-use protections without Jötunn, custom RPCs, server data, or networking.
- Ordinary build-piece clicks, the existing 30-second chest-backed building lease, and all existing features remain unchanged.
- Final automated verification passes with zero compiler warnings/errors, 133/133 pure-domain tests, and 65/65 static/repository safety checks. In-game validation passed; broader multiplayer, dedicated-server, workbench-extension, and load/unload edge-case testing remains incomplete.

## 1.0.0

Public release, preserving the live-validated 0.4.2 behavior without gameplay or configuration changes.

- Added independent, default-on controls for storage-backed crafting, storage-backed building, and aggregate storage totals in crafting, upgrade, and building requirement rows.
- Preserved explicit legacy building/crafting opt-outs through one-time migration to the renamed settings.
- Added adaptive exact requirement totals for longer values such as `45 / 172`, with affordability and flashing tied to the resources the active action is actually allowed to consume.
- Added a guarded 30-second sliding ownership lease for successful chest-backed building, with immediate remote manual-access preemption and immediate cleanup across every non-success lifecycle path.
- Fixed full-stack chest transfers and world drops leaving stale protected-item targets; partial transfers and internal player-inventory moves continue to retain protection.
- Pruned orphaned target records before storage actions so removed items no longer produce stale `target item missing` shortages.
- Final verification passes with zero compiler warnings/errors, 99/99 pure-domain tests, and 57/57 static/repository safety checks.
- In-game validation passed. Broader multiplayer, dedicated-server, workbench-extension, and load/unload edge-case testing remains incomplete.

## 0.4.2

Development-only build; not published.

- Split nearby-resource behavior into three independent, default-on settings: **Allow crafting from storage**, **Allow building from storage**, and **Show storage amounts in craft and build menus**.
- Existing `Enable building from nearby chests` and `Enable crafting from nearby chests` choices migrate once to the renamed permissions; explicit opt-outs remain off, and obsolete keys are removed from the saved configuration.
- Storage totals can now remain visible as exact `required / total available` values in crafting, upgrade, and building rows even when the corresponding action is restricted to player-held materials.
- Requirement affordability and red/white flashing now follow actual action permission on both code paths: player-only stock when that action's storage permission is off, aggregate player-plus-eligible-storage stock when it is on.
- Turning storage totals off preserves vanilla count text and typography while storage-backed crafting or building can remain enabled independently; turning both display and the relevant permission off leaves that requirement UI untouched.
- Preserved the connected-workbench mesh / fallback scope, exact aggregate accounting, adaptive long requirement-count fit, independent crafting/building consumption paths, and all 0.4.1 ownership and protected-item cleanup behavior.
- Automated tests cover all eight combinations of the three new booleans across both crafting and building UI decisions. Live in-game validation remains pending.

## 0.4.1

Development-only build; not published.

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

Version 0.2.1 reissues the approved nearby-resource release under a fresh Thunderstore version number so package managers can distinguish it from an earlier 0.2.0 upload. There are no gameplay changes from the approved build.

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
