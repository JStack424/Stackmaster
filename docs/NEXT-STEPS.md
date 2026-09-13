# Stackmaster Next Steps

This plan follows the behavior contract approved on September 11, 2026. [DESIGN-QUESTIONNAIRE.md](DESIGN-QUESTIONNAIRE.md) is the product source of truth; [RESEARCH.md](RESEARCH.md) retains the ecosystem evidence and provisional technical findings.

Environment validation, the complete local v0.1 gameplay implementation, and the solo clean-profile smoke gate are complete. On September 11, 2026, Joe explicitly approved the consolidated contract, authorized uninterrupted implementation, accepted the corrected Build 4 candidate after solo testing, and authorized the first public GitHub/Thunderstore release for co-op testing.

The **0.4.0 local test candidate** replaces four separate notions of “nearby” with one shared storage-scope resolver. Inside a canonical vanilla workbench build zone, it seeds from every zone containing the local player, traverses every transitively overlapping loaded workbench zone, and includes loaded supported vanilla containers only inside that exact connected union. It uses each station's current `GetStationBuildRange()` result and Valheim's horizontal, strict-boundary semantics rather than a hardcoded radius. Outside every valid mesh, deposit, replenishment, building, and crafting all use the existing configurable player-centered fallback radius (20 metres by default). Explicit-target-first and nearest routing, player-first exact minimum-chest resource planning, and loaded-only discovery remain unchanged. The per-chest `Auto-sort chest` checkbox affects sorting only and never removes a chest from this shared scope.

The mesh resolver uses the canonical `piece_workbench` prefab resolved through `ZNetScene`, verifies each loaded station's ZDO prefab identity, and is covered by the exact runtime compatibility gate. Mesh discovery is complete rather than subject to the ordinary nearby-action responsiveness cutoff. Read-only resource display still claims nothing. After any asynchronous ownership yield and again before mutation, actions recompute a single immutable scope snapshot and validate selected chest membership together with identity, access, use state, revisions, quantities, and ownership. Scope signatures invalidate same-frame/HUD captures when player location or connected workbench topology changes; no unloaded station or chest is represented.

Current 0.4.0 automated verification passes with zero compiler warnings/errors, 78/78 pure-domain tests, and 51/51 static/repository safety checks. Live solo, co-op host/client, dedicated-server, workbench-extension, topology-change, and sector load/unload testing remains pending. The candidate is local only and must not be presented as live-verified or published.

The **0.3.0 local test candidate** adds one per-chest `Auto-sort chest` checkbox. Its default is enabled; its disabled exception is kept only in local Unity preferences under a versioned key composed from the local player ID, current world UID, and the chest's stable ZDO ID. It never writes this preference to player custom data, ZDOs, or other shared world state. An enabled chest sorts on UI open and in the guarded `InventoryGui.Hide` prefix immediately before vanilla closes the UI. The close path uses only the cached chest being closed, never sorts the player inventory, requires the same exact vanilla type/owner/inventory checks as open-time sorting, and catches failures so vanilla close behavior continues. Missing identity, malformed local data, or failed local storage disables chest sorting safely. Deposit, replenishment, nearby build/craft, protection, and ownership flows are unchanged.

The **0.2.2 documentation-only release candidate** carries forward independently switchable building and crafting from nearby chests plus aggregate build/craft HUD totals without changing gameplay behavior. It reads stable `ZDOVars.s_items` snapshots without claiming ownership, hydrates detached item metadata from resolved prefabs, rejects incomplete chest snapshots as a whole, and fails open to vanilla UI behavior if nearby-resource display calculation fails. At action time the complete player-first plan selects only the minimum distinct chest set it needs, revalidates exact identity, access, revisions, use state, and contents, then withdraws exact quantities with rollback protection.

Valheim's owner-authorized response is asynchronous, so it cannot safely finish inside a synchronous Harmony build/craft prefix. Blind `ClaimOwnership` is not used. A remote-owned action cancels its first attempt without mutation while ownership is prepared, then asks the player to retry through the normal vanilla action path. Stackmaster keeps only ownership it demonstrably acquired available for a bounded 10-second retry window, completes reservations and any rollback before cleanup, and then returns the exact ZDO to Valheim's native unowned state after success, cancellation, validation failure, timeout, disable, or disconnect. If immediate release cannot be proven safe, cleanup retries later instead of touching uncertain ownership. A required chest rejected as busy or unavailable shows exactly `The required materials are currently in use`; denial, stale state, a changed plan, or any failed validation cancels without partial consumption.

Joe live-approved the corrected gameplay candidate on September 12, 2026 and confirmed it works solo and as a client-only mod connected to a vanilla server, with no Stackmaster installation required for the host, server, or other players. Current automated verification passes with zero compiler warnings/errors, 61/61 pure-domain tests, and 48/48 static/repository checks. These results and completed live checks do **not** establish universal multiplayer or network safety: broader co-op host, co-op guest, and unmodded dedicated-server coverage remains incomplete, and the public README retains a concise early-testing note.

## 1. Approved release boundary

The first public package is **Stackmaster** by **JStack424**:

- Thunderstore package/assembly direction: `Stackmaster`.
- BepInEx plugin GUID: `com.jstack424.stackmaster`.
- License: MIT.
- One plugin containing sorting, slot protection, depositing, and replenishment.
- Keyboard and mouse only in v0.1; controller support is explicitly deferred.
- Optional client-side installation, provided the full multiplayer matrix proves safe compatibility with vanilla hosts, guests, peers, and dedicated servers.
- Exactly three settings: auto-sort, radius, and action keybind.

Keep internal modules separate for testability and safe maintenance, but do not expose independent deposit/replenishment feature toggles or create separate packages in v0.1.

### Behavior implementation must match the approved contract

- Auto-sort runs when inventory opens and sorts the movable player inventory plus the opened chest alphabetically.
- The entire quick bar, equipped items, and each resolved protected stack remain fixed; protected partial stacks are untouched.
- Left Alt-click manages one-record/one-matching-stack protection and optional legal per-stack replenishment targets. Stackable-item prompts prefill and select the legal full-stack target for immediate Enter acceptance while still allowing a lower value or `0` for protection only. Preferred-slot matches win; moved stacks reattach deterministically; unrelated replacements never inherit.
- Configurable Left Alt + E performs the combined deposit/replenish action while deliberately targeting a valid container, or against the currently open vanilla chest without closing its UI.
- Current storage discovery is on demand and uniform across deposit, replenishment, building, and crafting: connected workbench mesh while inside a base, otherwise the configurable player-centered radius (20 metres by default).
- Only accessible vanilla containers participate; unknown/modded or in-use containers are skipped safely.
- Deposit only to containers that already hold a compatible item. Fill every compatible partial stack before creating a new stack, using the targeted chest first and then nearest-to-farthest routing.
- Replenish from the targeted chest first, then other eligible containers. Take partial available stock, report shortages, and route excess above targets back through normal deposit rules.
- Report compact unit totals and meaningful skips visually, with no added sounds.
- A failed container does not undo already completed safe transfers. A failed compatibility check disables all item-changing behavior for the session.

Any material departure from these rules returns to Joe before implementation continues.

## 2. Validate the Windows environment safely

### Step A — run the read-only inspector

From the repository root on Joe's Windows gaming PC:

```powershell
.\scripts\Inspect-StackmasterEnvironment.ps1
```

Default output:

```text
scripts\stackmaster-environment-report.json
```

The script:

- locates likely Steam libraries and Valheim app manifest `892970`;
- records Steam build metadata and `valheim.exe` version metadata when available;
- lists the managed assembly directory and a small relevant DLL inventory;
- locates likely r2modman Valheim profile folders;
- records BepInEx and Harmony DLL file/assembly versions and relevant paths;
- reports plugin DLL counts and the expected local reference/deploy directories;
- replaces a Windows user-profile prefix with `%USERPROFILE%` in output paths;
- reads no logs, config contents, credentials, or environment-variable collections;
- copies no assemblies, changes no game/profile/system settings, installs nothing, and makes no network requests;
- writes only the requested report file.

For nonstandard locations:

```powershell
.\scripts\Inspect-StackmasterEnvironment.ps1 `
  -ValheimPath 'D:\SteamLibrary\steamapps\common\Valheim' `
  -R2ModManDataPath "$env:APPDATA\r2modmanPlus-local\Valheim" `
  -ProfileName 'Stackmaster Dev'
```

Optional text output:

```powershell
.\scripts\Inspect-StackmasterEnvironment.ps1 -Format Text -OutputPath '.\stackmaster-environment-report.txt'
```

The output directory must already exist. Review the report before sharing it; generated reports are gitignored.

### Step B — resolve the environment from the report

Before choosing a compiler or creating project files, confirm:

- [x] Exact installed Valheim Steam build and executable version: build `25253764`, Unity `6000.0.75f1`.
- [x] Actual `Valheim_Data\Managed` location and assembly filenames.
- [x] Existing r2modman profile path: `VanillaPlus` (not the clean release-test profile).
- [x] Installed BepInEx DLL version: `5.4.23.5`; Thunderstore pack display version remains a later UI check.
- [x] HarmonyX (`0Harmony.dll`) version: `2.9.0.0`.
- [x] A separate clean profile must be created later; the existing `VanillaPlus` profile has five plugin DLLs.
- [x] Exact assembly inspection found sufficient Valheim APIs for v0.1 without a publicized assembly.
- [x] Plain BepInEx plus bundled HarmonyX is sufficient; no demonstrated Jötunn requirement exists.

The inspector intentionally does not read BepInEx logs or r2modman configuration contents. If a later validation step needs one, inspect that specific file manually and record only the minimum relevant fact.

### Step C — keep compile-time references private

The exact compile-time reference bundle is installed under the ignored `lib/local/StackmasterReferences/` directory, with a local checksum manifest. The project may instead receive an ignored `StackmasterReferencePath` override.

Never commit these assemblies, copy them into plugin output, or include them in a release ZIP. The project uses `<Private>false>` on each game/runtime reference, and the build fails if BepInEx or Unity reference DLLs appear beside `Stackmaster.dll`.

### Step D — use only the minimum toolchain

The environment and current Valheim Modding guidance support a `net48` deployable plugin. Pure planners use `netstandard2.0`, and automated tests use `net8.0`; .NET 8 is only the local build/test SDK. The rationale and private-reference boundary are recorded in [ADR 0001](architecture/0001-target-framework-and-reference-boundary.md).

Current status:

- [x] Choose the target framework from the installed environment and current template evidence.
- [x] Install a repository-external .NET 8 SDK under `~/workspace/toolchains/dotnet-8`; no system or gaming-PC installation.
- [x] Verify the compiler with the initial no-op `Stackmaster.dll` checkpoint and pure planner tests.
- [ ] Do not install Unity unless custom assets become an actual requirement.
- [ ] Do not copy or redistribute Valheim, Unity, BepInEx, Harmony, or Jötunn assemblies unless a package license and implementation requirement explicitly justify it.

## 3. Planned repository layout

Use a monorepo with one independent release package per eventual mod. Stackmaster is the only package now; do not create a separately installed “Core” dependency.

```text
valheim-qol-mods/
├── README.md
├── docs/
│   ├── DESIGN-QUESTIONNAIRE.md
│   ├── RESEARCH.md
│   └── NEXT-STEPS.md
├── Environment.props.example
├── Stackmaster.sln
├── src/
│   └── Stackmaster/
│       ├── Plugin.cs
│       ├── Compatibility/
│       ├── Configuration/
│       ├── Sorting/
│       ├── Protection/
│       ├── StorageAction/
│       ├── Patches/
│       ├── Notifications/
│       └── Infrastructure/
├── tests/
│   └── Stackmaster.Tests/
├── packages/
│   └── Stackmaster/
│       ├── manifest.json
│       ├── README.md
│       ├── CHANGELOG.md
│       └── icon.png
├── scripts/
│   ├── Collect-StackmasterReferences.ps1
│   ├── Inspect-StackmasterEnvironment.ps1
│   ├── build.sh
│   ├── dotnet.sh
│   └── package.py
└── artifacts/                 # ignored build/package output
```

### Shared-code policy

Keep helpers internal to Stackmaster. If a later mod truly needs the same game-agnostic code, extract source or a compile-time project reference while continuing to ship self-contained plugins where licensing permits. Add a separately installed runtime dependency only when independent versioning provides real value.

## 4. Architecture for safe implementation

Separate pure policy from Valheim/Unity mutation:

```text
Plugin entry point
├── Compatibility gate
├── Configuration
├── Feature coordinator
│   ├── Sorting
│   ├── Protection and persistence
│   └── Deposit/replenish action
├── Game adapters
│   ├── Player inventory
│   ├── Container discovery/access
│   ├── Character data persistence
│   └── Notification UI
└── Pure domain logic
    ├── Sort planner
    ├── Transfer planner
    ├── Replenishment planner
    └── Validation/result models
```

Rules:

- Pure planners accept immutable snapshots and return deterministic plans/results.
- Only adapters touch Unity/Valheim objects.
- All chest mutations pass through one audited executor.
- Every Harmony patch names its owning feature and uses the narrowest safe seam.
- Prefer public game methods, then narrow Prefix/Postfix patches; transpilers are a last resort.
- Discovery runs only for inventory opening or the explicit storage action, never continuously.
- Revalidate item identity, quantity, target capacity, access, distance, and ownership immediately before each mutation.
- Preserve item identity and metadata rather than recreating items from names.
- Character protection data is versioned and validated; malformed data fails without moving items.
- If startup compatibility is unsafe, install no item-changing behavior and clearly warn/log rather than running a partial mod.

## 5. Implementation milestones

### Milestone 0 — environment and initial compile checkpoint

- [x] Complete the inspection report and record the validated versions/paths.
- [ ] Back up the character and world selected for later gameplay testing.
- [x] Create the solution/project using the validated target framework and local references.
- [x] Add stable metadata: Stackmaster, `com.jstack424.stackmaster`, one shared version source.
- [x] Log plugin version, game version, and compatibility-gate result.
- [x] Build without copying local game DLLs into output.
- [x] Separate skeleton-only r2modman smoke test superseded by Joe's authorization to use the integrated clean-profile gate.
- [x] Commit before adding gameplay behavior (`7a9a6c2`).

Exit criterion: the exact environment is documented and the compile/reference boundary is verified locally; integrated loading is covered by the combined Windows gate.

### Milestone 1 — pure sorting and transfer models

- [x] Define item identity, stack compatibility, protected-stack assignment, inventory, container, and result models.
- [x] Implement stable alphabetical sorting and compatible stack consolidation.
- [x] Implement deterministic routing: targeted chest first, then nearest to farthest, partial stacks before new stacks, stable slot order.
- [x] Implement replenishment, partial shortages, target trimming, and no-destination results.
- [x] Test empty/full inventories, duplicates, metadata differences, max stacks, protected-stack reconciliation, shortages, overflow, and no item-count drift.

Exit criterion: comprehensive automated tests pass without launching Valheim.

### Milestone 2 — local sorting integration

- [x] Identify the current inventory-open hook and supported mutation APIs.
- [x] Add the in-inventory auto-sort checkbox along the panel's bottom border below the item slots, enabled by default, with a centered symmetric X and full label-width click target.
- [x] Sort the movable player area and an owner-authorized opened vanilla chest once per inventory open.
- [x] Preserve the entire quick bar, equipped items, resolved protected stacks, and protected partial stacks.
- [x] Trigger required vanilla inventory/UI refresh methods.
- [x] Verify deterministic conservation and idempotence in the local automated gate; repeat in Valheim during the combined Windows gate.

Exit criterion: source integration, automated losslessness checks, and solo in-game confirmation are complete; multiplayer confirmation remains open.

### Milestone 3 — protection UI and character persistence

- [x] Add Left Alt-click Protect only / Protect with target / Unprotect behavior.
- [x] Prompt stackable items immediately for a legal single-slot target quantity, with the full legal stack size prefilled/selected for Enter acceptance (`0` still means Protect only).
- [x] Store versioned per-character protection identity, preferred slot, and optional target data across worlds; migrate the safe subset of v1 exact-slot records.
- [x] Reconcile each record to exactly one matching stack, preferring its current slot and otherwise choosing in deterministic row-major order; never protect a nonmatching replacement.
- [x] Show a noninteractive soft teal border plus a top-left target quantity in the exact border color or protection-only lock on resolved stacks, clear of the vanilla lower-right stack quantity.
- [x] Validate saved data before use and skip malformed or incompatible records safely.
- [x] Verify one complete quit/relaunch with the same character preserves choices in the solo smoke gate.

Exit criterion: serialization, v1 migration, malformed-record handling, matching-stack reconciliation, replacement-item non-inheritance, deterministic duplicates, merge survivors, dormant-record behavior, and solo quit/relaunch persistence pass.

### Milestone 4 — discovery and storage planning

- [x] Discover supported loaded vanilla containers only when needed, using the connected canonical-workbench mesh while inside a base and the configured player-centered radius outside every mesh.
- [x] Filter by known vanilla type, access, state, active-scope membership, ownership, and active use.
- [x] Use the deliberately targeted container as first routing priority.
- [x] Build and validate the dry-run transfer plan before mutation.
- [x] Apply the bounded ordinary-action inspection budget only to fallback-radius searches; always enumerate a complete loaded workbench mesh rather than presenting a partial mesh as complete.
- [x] Unit-test stale-plan rejection, conservation, and budget exhaustion boundaries.

Exit criterion: the local dry-run path explains moves and meaningful skips; gameplay profiling remains in the Windows gate.

### Milestone 5 — network-safe execution

- [x] Execute through `Inventory.MoveItemToThis` and request ownership through each container's owner-authorized `StackAll` / `RPC_RequestStack` handshake while suppressing the matching vanilla transfer response.
- [x] Intercept only matching ownership responses so background containers are not opened in the UI.
- [x] Refresh from synchronized ZDO state, then revalidate source, destination, identity, capacity, access, ownership, and use state before every move.
- [x] Enforce exact before/after unit conservation; isolate ordinary per-container failures and stop the remaining action on an unexpected postcondition.
- [x] Skip containers currently in use and bound ownership waits to two seconds.
- [x] Verify inventory/container notifications in live solo play.
- [ ] Verify inventory/container synchronization in live co-op host, co-op guest, and unmodded dedicated-server play.
- [ ] Exercise multiplayer access denial, destroyed targets, disconnects, and simultaneous clients.
- [ ] Confirm the optional client-only install contract through the full unmodded-peer/server matrix.

Exit criterion: source follows the inspected vanilla authority path and solo behavior passes; only the remaining Windows multiplayer matrix can establish observed synchronization safety.

### Milestone 6 — configuration and feedback

- [x] Bind exactly five global settings: auto-sort, 20-metre outside-base fallback radius, Left Alt + E action binding, and independent build/craft-from-chests toggles. Keep per-chest auto-sort as local preference data rather than a sixth setting.
- [x] Clamp the radius and report unsafe startup compatibility clearly.
- [x] Add the targeted-container tooltip and compact visual result popup.
- [x] Run the configured storage shortcut against an already open vanilla chest without closing its UI, while preserving the closed-UI targeted-container path.
- [x] Count individual units and name shortages, in-use/failed containers, and incomplete searches without listing every success.
- [x] Add no custom sounds and make no controller UI/binding claims.

Local gameplay checkpoints: integrated implementation `03d8c4d`; synchronized-container hardening `4d11275`. The working tree must remain clean and the complete build gate must pass again after documentation changes.

### Milestone 7 — exact package candidate

- [x] Create original 256×256 RGBA chest-stack icon artwork.
- [x] Write the public README, MIT license, and changelog.
- [x] Pin the current Thunderstore dependency: `denikson-BepInExPack_Valheim-5.4.2350` (BepInEx runtime 5.4.23.5).
- [x] Build one deterministic ZIP from the committed allowlist.
- [x] Run automated and independent structural validation.
- [ ] Import the exact public ZIP into a fresh r2modman profile with declared dependencies and intended defaults.
- [ ] Archive the exact public ZIP, sidecar checksum, validation record, and source commit locally.

Updating an older package and clean uninstall are **not** v0.1 release gates.

### Milestone 8 — manual first release

- [x] Joe reviews the release direction, name, description, icon, compatibility position, and changelog scope and authorizes publication.
- [ ] Create/confirm the public GitHub repository with GitHub Issues enabled.
- [x] Validate the manifest and README against Thunderstore's current package rules.
- [ ] Upload to the JStack424 Thunderstore team with the **AI Generated** category selected, as required for this significantly AI-assisted release.
- [ ] Install the published package through r2modman after cache propagation and verify startup plus one core workflow.
- [ ] Tag the exact source commit only after publication is confirmed.

Publication remains a separate explicit action so local building cannot accidentally release. Joe authorized this first public release on September 11, 2026; the live GitHub and Thunderstore actions are tracked separately from local preparation.

## 6. v0.1 release test matrix

Each test run records:

```text
Date/time:
Source commit:
Package version:
Package ZIP checksum:
Valheim Steam build/executable version:
BepInEx pack and DLL version:
Harmony version:
Jötunn version (or absent):
r2modman version/profile:
Client OS/input:
Server type and version:
Installed mod list:
Scenario:
Expected result:
Actual result:
Item-count/metadata check:
Log excerpt location:
Pass/fail:
```

Required gates:

- [x] Automated sorting/transfer and repository-boundary tests pass: protection persistence, compatible merging, partial-stack priority, routing, replenishment, shortages, excess, budget exhaustion, compatibility-gate-before-patching, stale-state rejection, and no item-count drift.
- [x] Clean-profile startup contains no Stackmaster exception in the passed solo smoke gate.
- [x] Basic sorting/deposit/replenishment smoke tests pass in solo play.
- [x] With a vanilla chest open, the configured storage shortcut uses that chest as the target, deposits/replenishes normally, and leaves the chest UI open.
- [x] A stackable-item target prompt opens with the full legal stack size prefilled/selected; Enter accepts it immediately, a lower legal value replaces it, `0` selects protection only, and non-stackable items remain protection-only.
- [ ] The same basic flow passes while hosting co-op with an unmodded peer; state stays synchronized.
- [ ] The same basic flow passes as a guest of a vanilla host; state stays synchronized.
- [ ] The same basic flow passes on a vanilla dedicated server with no server-side Stackmaster installation; unmodded peers remain compatible.
- [x] The solo smoke gate's full game quit/relaunch preserves protected-stack identities, preferred slots, and targets for the same character.
- [ ] A normal-sized base at the 20-meter default remains acceptably responsive and supplies profiling data for the internal time budget.
- [ ] A fresh r2modman profile imports the exact ZIP and launches with declared dependencies and intended defaults.
- [ ] The exact ZIP contains valid required Thunderstore files and no game assemblies, local paths, credentials, tests, or development debris.
- [ ] Any observed loss, duplication, crash, corrupt metadata, or synchronization error blocks release.

Conflict testing is limited to BepInEx, Stackmaster, and declared dependencies in a clean profile. No v0.1 claim is made for coexistence with other inventory/gameplay mods. A separate oversized-base stress test, controller testing, upgrade testing, uninstall testing, cross-world persistence test, and separate-character isolation test are not v0.1 release gates.

Before claiming compatibility after a later Valheim update, rerun the supported smoke gate. The compatibility gate itself must have automated coverage proving that an unsafe version disables every item-changing path and displays/logs the reason.

## 7. Package automation

`scripts/package.py` refuses a dirty or uncommitted working tree and fails on:

- any ZIP path outside the exact allowlist: root `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`, and `plugins/Stackmaster/Stackmaster.dll`;
- invalid JSON or release metadata that differs from the approved name, version, website, description, or dependency;
- an icon that is not exactly 256×256 8-bit RGBA PNG;
- unexpected root nesting or directory entries;
- private machine-path markers in the DLL;
- Valheim/Unity/BepInEx/Harmony/Jötunn assemblies, `Stackmaster.Core.dll`, PDBs, tests, source, or development debris;
- a plugin file that is not a Windows PE/.NET assembly or lacks the expected Stackmaster identity markers.

The script reads only committed package metadata and the rebuilt Release DLL, validates them, creates a deterministic timestamped ZIP from the explicit allowlist, reopens and verifies the final archive, and prints the source commit plus ZIP/member checksums. It has no upload capability.

The MIT `LICENSE` is committed at the repository root. It is not duplicated into the package because Thunderstore requires `manifest.json`, `icon.png`, and `README.md` at ZIP root, explicitly accepts `CHANGELOG.md`, and permits additional mod-specific files without requiring a license file.

Publishing remains a separate explicit action so building cannot accidentally release.

## 8. Current state and next handoff

- Joe reported that an earlier 0.2.0 package was accidentally uploaded to Thunderstore, then successfully uploaded the corrected 0.2.1 package. The GitHub release state has not been changed by this packaging work.
- The 0.2.2 candidate changes documentation and release identity only; there are no gameplay behavior changes from the live-approved 0.2.1 candidate.
- The carried-forward release includes independently switchable nearby-chest building and crafting, aggregate requirement totals, ownership-independent display snapshots, complete detached-item hydration, fail-open UI protection, exact player-first/minimum-chest planning, rollback, and bounded ownership cleanup.
- Remote ownership uses Valheim's owner-authorized asynchronous flow rather than blind `ClaimOwnership`: the first attempt prepares required ownership and consumes nothing, and one normal retry may perform the action.
- Ownership demonstrably acquired by Stackmaster remains available for a 10-second retry window. Cleanup then returns it to Valheim's native unowned state after success, cancellation, failure, timeout, disable, or disconnect, retrying later if immediate release cannot be proven safe. Unrelated or already-local chests are never released.
- Automated verification passes: zero compiler warnings/errors, 61/61 pure-domain tests, and 48/48 static/repository checks.
- Exact rollback, vanilla double-charge suppression, original Viking-chest icon, JStack424 identity, MIT license, external BepInEx dependency, deterministic five-file package allowlist, and a concise early-public-testing note are preserved.
- Joe confirmed solo use and client-only use on a vanilla server without requiring Stackmaster on the host, server, or other players. Broader co-op host, co-op guest, and unmodded dedicated-server coverage remains incomplete; 0.2.2 does not claim universal multiplayer safety.

**Further 0.2.2 validation:**

1. Confirm ownership-independent build, crafting, and upgrade totals without opening contributing chests or changing their owner.
2. Confirm the first remote-owned attempt consumes nothing, the retry consumes exact quantities once, and every used chest opens for vanilla peers immediately afterward and after the modded player logs out.
3. Trigger ownership preparation without retrying; after the 10-second retry window and any safe cleanup retries have completed, confirm vanilla peers can open every involved chest before and after logout.
4. Repeat immediate logout after ownership preparation and immediately after a successful retry; every tracked chest must become usable by vanilla peers.
5. Confirm only the minimum required chest set is touched and unrelated nearby chests retain normal behavior.
6. Exercise busy, denied, stale, changed, inaccessible, out-of-range, timeout, and rollback paths; every failure must cancel without partial consumption or stranded ownership.
7. Repeat building, crafting, upgrades, multi-craft, duplicate requirements, quality constraints, ordinary sorting, deposit, replenishment, protection, open-container actions, and settings-off behavior.
8. Continue the solo, co-op host, co-op guest, and unmodded dedicated-server matrix. Treat any loss, duplication, crash, corruption, incorrect ownership transfer, partial charge, unrelated chest claim, or synchronization disagreement as release-blocking.

**Next handoff:** Deliver the verified `JStack424-Stackmaster-0.2.2.zip` candidate. Any GitHub or Thunderstore publication remains a separate explicit action; package preparation itself has no upload capability.
