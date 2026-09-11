# Stackmaster Next Steps

This plan follows the behavior contract approved on September 11, 2026. [DESIGN-QUESTIONNAIRE.md](DESIGN-QUESTIONNAIRE.md) is the product source of truth; [RESEARCH.md](RESEARCH.md) retains the ecosystem evidence and provisional technical findings.

The current phase is **environment validation only**. Do not install a toolchain, copy game assemblies, scaffold gameplay code, modify Valheim/r2modman, create a public repository, or publish a package until the relevant later step is approved.

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
- Quick-bar, equipped, and exact protected slots remain fixed; protected partial stacks are untouched.
- Left Alt-click manages protection and optional legal per-slot replenishment targets.
- Configurable Left Alt + E performs the combined deposit/replenish action only while deliberately targeting a valid container.
- Discovery is player-centered, on demand, and 20 meters by default.
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

- [ ] Exact installed Valheim Steam build and executable version.
- [ ] Actual `Valheim_Data\Managed` location and assembly filenames.
- [ ] Active r2modman profile path.
- [ ] Installed BepInEx DLL version and Thunderstore pack version where r2modman displays it.
- [ ] HarmonyX (`0Harmony.dll`) version supplied by that profile.
- [ ] Whether the current clean profile already exists or must later be created.
- [ ] Whether current public game APIs are sufficient without a publicized assembly.
- [ ] Whether plain BepInEx is sufficient or Jötunn provides a demonstrated requirement.

The inspector intentionally does not read BepInEx logs or r2modman configuration contents. If a later validation step needs one, inspect that specific file manually and record only the minimum relevant fact.

### Step C — create local reference settings later

After the paths are verified, create an ignored `Environment.props` locally from a committed example:

```xml
<Project>
  <PropertyGroup>
    <ValheimInstall>LOCAL_PATH_HERE</ValheimInstall>
    <ValheimManaged>LOCAL_PATH_HERE</ValheimManaged>
    <BepInExCore>LOCAL_PATH_HERE</BepInExCore>
    <ModDeployPath>LOCAL_PATH_HERE</ModDeployPath>
  </PropertyGroup>
</Project>
```

Do not commit the filled local file. Reference local assemblies in place; do not copy them into tracked paths.

### Step D — choose and install only the minimum toolchain later

The research found conflicting pre-1.0 guidance: raw BepInEx examples commonly use .NET Framework 4.8, while Jötunn guidance has named 4.6.2. Neither is a decision for Stackmaster until the current assemblies and a current working template are inspected.

When separately authorized:

- [ ] Choose the target framework from the installed environment and current template evidence.
- [ ] Install only the matching .NET/Visual Studio or Mono/MSBuild components.
- [ ] Verify the compiler with a harmless minimal build.
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
│   ├── Inspect-StackmasterEnvironment.ps1
│   ├── build.ps1
│   ├── package.ps1
│   └── verify-package.ps1
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

### Milestone 0 — environment and harmless skeleton

- [ ] Complete the inspection report and record the validated versions/paths.
- [ ] Back up the character and world selected for later gameplay testing.
- [ ] Create the solution/project using the validated target framework and local references.
- [ ] Add stable metadata: Stackmaster, `com.jstack424.stackmaster`, one shared version source.
- [ ] Log plugin version, game version, and compatibility-gate result.
- [ ] Build without copying local game DLLs into output.
- [ ] Load in a dedicated clean r2modman profile and verify startup/shutdown without gameplay patches.
- [ ] Commit before adding gameplay behavior.

Exit criterion: the exact environment is documented and a harmless plugin loads cleanly.

### Milestone 1 — pure sorting and transfer models

- [ ] Define item identity, stack compatibility, protected-slot, inventory, container, and result models.
- [ ] Implement stable alphabetical sorting and compatible stack consolidation.
- [ ] Implement deterministic routing: targeted chest first, then nearest to farthest, partial stacks before new stacks, stable slot order.
- [ ] Implement replenishment, partial shortages, target trimming, and no-destination results.
- [ ] Test empty/full inventories, duplicates, metadata differences, max stacks, protected slots, shortages, overflow, and no item-count drift.

Exit criterion: comprehensive automated tests pass without launching Valheim.

### Milestone 2 — local sorting integration

- [ ] Identify the current inventory-open hook and supported mutation APIs.
- [ ] Add the in-inventory auto-sort checkbox, enabled by default.
- [ ] Sort the movable player area and opened vanilla chest once per inventory open.
- [ ] Preserve quick bar, equipped items, protected slots, and protected partial stacks.
- [ ] Trigger required vanilla inventory/UI refresh methods.
- [ ] Verify repeated sorting preserves item count and metadata.

Exit criterion: local sorting is deterministic, idempotent, and lossless.

### Milestone 3 — protection UI and character persistence

- [ ] Add Left Alt-click choices: Protect only, Protect with target for stackables, and Unprotect.
- [ ] Prompt immediately for a legal single-slot target quantity.
- [ ] Store protection against exact inventory slots per character across worlds.
- [ ] Validate saved data before use and fail safely on malformed or incompatible state.
- [ ] Verify one complete quit/relaunch with the same character preserves choices.

Exit criterion: exact-slot protection and targets persist and cannot silently affect the wrong slot.

### Milestone 4 — discovery and storage planning

- [ ] Discover supported vanilla containers only when needed, centered on the player, within the configured radius.
- [ ] Filter by access, state, distance, ownership, and active use.
- [ ] Use the deliberately targeted container as first routing priority.
- [ ] Build a complete dry-run result before mutation where possible.
- [ ] Apply a measured time budget while preserving routing order and an explicit partial-search result.
- [ ] Unit-test stale snapshots and budget exhaustion.

Exit criterion: dry-run plans explain exactly what will move and why anything is skipped.

### Milestone 5 — network-safe execution

- [ ] Execute through authoritative vanilla inventory/container paths.
- [ ] Revalidate before every move and preserve completed safe transfers if a later container fails.
- [ ] Skip containers in use by another player.
- [ ] Verify inventory/container notifications and synchronization.
- [ ] Exercise access denial, destroyed targets, disconnects, and simultaneous clients.
- [ ] Confirm whether vanilla paths preserve the optional client-only install contract; revisit architecture before adding any RPC or server requirement.

Exit criterion: the solo, host, guest, and unmodded dedicated-server matrix shows synchronized state with no observed loss or duplication.

### Milestone 6 — configuration and feedback

- [ ] Bind exactly three settings: auto-sort, 20-meter radius, and Left Alt + E action binding.
- [ ] Make invalid configuration fail or clamp safely with clear diagnostics.
- [ ] Add the targeted-container tooltip and compact visual result popup.
- [ ] Count individual units and name shortages, in-use containers, failed containers, and incomplete searches without listing every success.
- [ ] Add no custom sounds and no controller UI/binding claims.

### Milestone 7 — exact package candidate

- [ ] Create original 256×256 chest-stack icon artwork.
- [ ] Write the public README, MIT license, and changelog.
- [ ] Pin live dependency versions from the final tested profile.
- [ ] Build one deterministic ZIP from an allowlist.
- [ ] Run automated structural validation.
- [ ] Import the ZIP into a fresh r2modman profile with declared dependencies and intended defaults.
- [ ] Archive the tested ZIP, checksum, test record, and source commit locally.

Updating an older package and clean uninstall are **not** v0.1 release gates.

### Milestone 8 — manual first release

- [ ] Joe reviews the final name, description, icon, compatibility claims, changelog, and exact candidate.
- [ ] Create/confirm the public GitHub repository and GitHub Issues only with Joe's approval.
- [ ] Validate the manifest and README against Thunderstore's current rules.
- [ ] Upload manually to the JStack424 Thunderstore team only with Joe's approval.
- [ ] Install the published package through r2modman after cache propagation and verify startup plus one core workflow.
- [ ] Tag the exact source commit only after publication is confirmed.

No automated publishing until a manual release process is proven and Joe asks for it.

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

- [ ] Automated sorting/transfer tests pass: protection, compatible merging, partial-stack priority, routing, replenishment, shortages, excess, budget exhaustion, compatibility disablement, and no item-count drift.
- [ ] Clean-profile startup contains no Stackmaster exception.
- [ ] Basic sorting/deposit/replenishment smoke tests pass in solo play.
- [ ] The same basic flow passes while hosting co-op with an unmodded peer; state stays synchronized.
- [ ] The same basic flow passes as a guest of a vanilla host; state stays synchronized.
- [ ] The same basic flow passes on a vanilla dedicated server with no server-side Stackmaster installation; unmodded peers remain compatible.
- [ ] One full game quit/relaunch preserves protected slots and targets for the same character.
- [ ] A normal-sized base at the 20-meter default remains acceptably responsive and supplies profiling data for the internal time budget.
- [ ] A fresh r2modman profile imports the exact ZIP and launches with declared dependencies and intended defaults.
- [ ] The exact ZIP contains valid required Thunderstore files and no game assemblies, local paths, credentials, tests, or development debris.
- [ ] Any observed loss, duplication, crash, corrupt metadata, or synchronization error blocks release.

Conflict testing is limited to BepInEx, Stackmaster, and declared dependencies in a clean profile. No v0.1 claim is made for coexistence with other inventory/gameplay mods. A separate oversized-base stress test, controller testing, upgrade testing, uninstall testing, cross-world persistence test, and separate-character isolation test are not v0.1 release gates.

Before claiming compatibility after a later Valheim update, rerun the supported smoke gate. The compatibility gate itself must have automated coverage proving that an unsafe version disables every item-changing path and displays/logs the reason.

## 7. Package automation to build later

`verify-package.ps1` should fail on:

- missing root `manifest.json`, `README.md`, `CHANGELOG.md`, `LICENSE`, plugin DLL, or `icon.png`;
- invalid JSON, missing manifest fields, invalid package name/version/description length, or placeholder dependency versions;
- icon not exactly 256×256 PNG;
- unexpected root nesting;
- local path/property files, generated environment reports, or credentials;
- Valheim/Unity/BepInEx/Harmony/Jötunn DLLs unless deliberately licensed and required;
- test assemblies, source, or development debris;
- mismatch between manifest, plugin, assembly, and changelog versions.

`package.ps1` should:

1. clean a staging directory;
2. copy only an explicit allowlist;
3. inject one validated version into all package surfaces;
4. run the verifier;
5. create a deterministic ZIP under `artifacts/`;
6. print the artifact path and checksum;
7. never upload.

Publishing remains a separate explicit action so building cannot accidentally release.

## 8. Current state and next handoff

- Approved contract: complete.
- Read-only Windows inspector: prepared; not yet run on Joe's gaming PC.
- Repository: initialized and versioned on `main`.
- SDK/compiler: not installed here.
- Game/profile files: not accessed or copied here.
- Valheim/r2modman: not modified.
- Gameplay code/project scaffold: not created.
- Public GitHub repository and Thunderstore package: not created or published.

**Next handoff:** Joe runs `scripts\Inspect-StackmasterEnvironment.ps1` on the gaming PC and returns the sanitized report. That report determines the exact framework, references, development-profile steps, and harmless plugin scaffold.
