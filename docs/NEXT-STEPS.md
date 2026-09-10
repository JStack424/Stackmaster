# Next Steps

This is the implementation plan after the 2026-09-10 research pass. It intentionally stops before installing a toolchain, reading local game files, modifying Valheim, or publishing anything.

## 1. Define the first release

Recommended first package: **InventoryStorage** — one plugin with independently configurable inventory-sorting and chest auto-stack modules.

Start with one package because Joe wants one cohesive workflow and many configuration options. Preserve an internal boundary between the two features because their compatibility contracts may differ:

- inventory sorting is likely local/client-only;
- chest auto-stack mutates networked container state and needs deeper multiplayer validation.

Split them into separate Thunderstore packages later only if testing shows they need different install requirements, dependencies, release timing, or compatibility claims.

### Decisions for Joe

The suggested default is shown in **bold**.

#### Inventory sorting

- Trigger: **explicit hotkey plus an inventory-screen button**, hotkey only, button only, or automatic?
- Scope: **backpack inventory only** for v0.1, or also an open container?
- Hotbar: **never move hotbar slots by default**; should users be allowed to opt in?
- Equipped items: **never move them**.
- Favorites/pins: should the mod supply a way to protect arbitrary slots/items in v0.1, or begin with hotbar/equipped protection only?
- Rule: category → name → quality is the proposed first deterministic order. Does Joe want a different mental model?
- Naming: sort by **display/localized name** or stable internal prefab name? Display name is intuitive; internal name is stable across language changes.
- Stack consolidation: **separate option**, not an implicit consequence of sorting.
- Controller: should a controller binding be required for the first public version?

#### Chest auto-stack

- Trigger: **explicit hotkey/button** for v0.1, or automatic when arriving at base/opening inventory?
- Destination rule: **fill existing matching stacks only** by default, or allow creating a new stack in any eligible nearby chest?
- Radius: pick an initial value after measuring normal base/chest spacing in game rather than guessing.
- Container eligibility: player-built chests only at first, or carts, ships, personal chests, and later storage too?
- Permissions: **respect every vanilla access/ownership check and skip inaccessible containers**.
- Routing priority: nearest container, most-full matching stack, container name/tag, or a configurable rule?
- Ignore/protect model: item names, categories, inventory slots, container opt-out, or a combination?
- Feedback: brief HUD summary, sound, chat text, log only, or configurable?
- Partial success: **move everything still valid, report skipped items**, rather than all-or-nothing across every chest.

#### Product identity

Choose before public packaging, not before local prototyping:

- plugin/package display name;
- stable plugin GUID;
- Thunderstore team namespace;
- permanent package name;
- public source/homepage URL;
- license;
- icon/visual identity.

Thunderstore package identity is durable. A temporary name should not be uploaded “just to test.”

## 2. Validate the local development environment

Perform these steps only when Joe is ready to start implementation.

### Record versions first

- [ ] Back up the character and world that will be used for testing.
- [ ] Record the exact Valheim version shown by the installed game.
- [ ] Create a new r2modman profile dedicated to development.
- [ ] Install the current `denikson-BepInExPack_Valheim` in that profile.
- [ ] Launch once through r2modman with BepInEx only.
- [ ] Confirm a clean startup and locate `BepInEx/LogOutput.log`.
- [ ] Record the installed BepInEx pack version, embedded BepInEx version, and Harmony version.
- [ ] If considering Jötunn, install it only in the development profile and confirm its version and clean startup separately.

### Discover paths; do not assume them

Use r2modman's **Settings → Browse profile folder** and Steam's **Browse local files** actions. Record paths in an ignored local file such as `Environment.props`.

Expected values:

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

Do not commit the filled local file. Commit only a `.example` template without machine-specific values.

### Inspect current assemblies and templates

- [ ] List the actual managed assembly filenames under `Valheim_Data/Managed`.
- [ ] List the BepInEx core assemblies in the active profile.
- [ ] Confirm which DLL defines Valheim inventory/container classes after 1.0.
- [ ] Inspect the current Jötunn Mod Stub target framework and build process.
- [ ] Confirm whether a publicized assembly is necessary; avoid it if public methods are sufficient.
- [ ] Choose the compiler/SDK only after those checks.

Current guides conflict by workflow: the raw-BepInEx community guide recommends .NET Framework 4.8, while Jötunn's guide names 4.6.2. Both predate final validation against Valheim 1.0. Preserve the selected current template's settings and verify them locally rather than choosing from these notes alone.

### Install only the minimum toolchain

When explicitly authorized:

- [ ] Install a compatible .NET/Visual Studio or Mono/MSBuild toolchain.
- [ ] Verify it with a tiny build before generating the real project.
- [ ] Do not install Unity unless custom assets become an actual requirement.
- [ ] Do not copy game assemblies into tracked repository paths.

## 3. Recommended repository layout

Use a monorepo with one independent release package per eventual mod. Do not create a separately deployed “Core” mod yet.

```text
valheim-qol-mods/
├── README.md
├── docs/
│   ├── RESEARCH.md
│   └── NEXT-STEPS.md
├── Directory.Build.props
├── Directory.Packages.props
├── Environment.props.example
├── Valheim.QoL.sln
├── src/
│   └── Valheim.QoL.InventoryStorage/
│       ├── Plugin.cs
│       ├── Configuration/
│       ├── InventorySorting/
│       ├── ChestAutoStack/
│       ├── Patches/
│       ├── Notifications/
│       └── Infrastructure/
├── tests/
│   └── Valheim.QoL.InventoryStorage.Tests/
├── packages/
│   └── InventoryStorage/
│       ├── manifest.json
│       ├── README.md
│       ├── CHANGELOG.md
│       └── icon.png
├── scripts/
│   ├── build.ps1
│   ├── package.ps1
│   └── verify-package.ps1
└── artifacts/                 # ignored build/package output
```

### Why this layout

- `src/<mod>` owns runtime code and can become its own package without a repository split.
- `packages/<mod>` owns only public Thunderstore content.
- `tests/<mod>` exercises pure algorithms without loading Unity where possible.
- `scripts` can build and validate every package consistently.
- `artifacts` is disposable and ignored.
- local paths remain in an ignored environment file.

### Shared code policy

For the first mod, keep helpers internal to the plugin. If a second mod later needs the same code:

1. identify the genuinely shared, game-agnostic subset;
2. extract source or a compile-time project reference;
3. continue shipping each plugin self-contained where licensing allows;
4. create a separately installed runtime dependency only if independent versioning is truly valuable.

This avoids forcing players to install a tiny shared DLL that exists only because the repository was organized early.

## 4. Architecture for safe splitting

Within the first plugin, use explicit interfaces between game access and pure policy:

```text
Plugin entry point
├── Configuration
├── Feature coordinator
│   ├── InventorySorting feature
│   └── ChestAutoStack feature
├── Game adapters
│   ├── Player inventory adapter
│   ├── Container discovery/access adapter
│   └── Notification adapter
└── Pure domain logic
    ├── Sort planner
    ├── Transfer planner
    └── Validation/result models
```

Rules:

- feature modules do not patch each other's methods;
- pure planners accept snapshots and return plans/results;
- only adapters touch Valheim/Unity objects;
- chest mutations occur in one audited executor;
- every Harmony patch names its owning feature;
- disabling a feature avoids installing its patches where practical;
- no feature stores world data unless its design explicitly requires it.

If sorting and auto-stack split later, move each feature plus its patches/config into a new plugin project while retaining or duplicating the small adapters. Their Thunderstore manifests can then declare different dependencies and compatibility rules.

## 5. Implementation milestones

### Milestone 0 — verified skeleton

Goal: one harmless plugin that loads on the current game.

- [ ] Create solution/project with the validated target framework.
- [ ] Reference local assemblies through ignored path properties.
- [ ] Add stable plugin metadata and version source.
- [ ] Bind one test configuration value.
- [ ] Log plugin version and detected game version.
- [ ] Build without copying local game DLLs into output.
- [ ] Load in the clean profile and verify one startup log line.
- [ ] Add a package validator skeleton.
- [ ] Commit before adding gameplay patches.

Exit criterion: clean BepInEx startup and shutdown, no game behavior changed.

### Milestone 1 — pure inventory sort planner

Goal: deterministic, testable sorting without Unity mutation.

- [ ] Define item snapshot, protected-slot, and sort-key models.
- [ ] Implement a stable sort plan.
- [ ] Test empty, full, duplicate, partial-stack, quality, durability, and protected-slot cases.
- [ ] Ensure the plan conserves every source item identity and quantity.
- [ ] Commit the pure logic before game integration.

Exit criterion: comprehensive unit tests pass without Valheim running.

### Milestone 2 — local inventory sorting integration

Goal: safely apply a sort plan to the local player's inventory.

- [ ] Inspect current 1.0.7 inventory APIs and choose the narrowest hook/command path.
- [ ] Add explicit hotkey/button trigger.
- [ ] Refuse to run during invalid UI/player states.
- [ ] Preserve equipped, hotbar, pinned, favorited, and custom-data items according to config.
- [ ] Trigger required vanilla inventory/UI refresh methods.
- [ ] Add a concise operation result and bounded diagnostic logs.
- [ ] Test local, host, and guest roles.

Exit criterion: repeated sorting cannot lose, duplicate, or mutate item metadata.

### Milestone 3 — container discovery and transfer planner

Goal: plan auto-stack operations without changing game state.

- [ ] Discover only supported nearby containers.
- [ ] Filter by range, type, access, state, and config.
- [ ] Snapshot candidate targets and capacities.
- [ ] Prioritize compatible partial stacks.
- [ ] Optionally plan empty-slot moves when enabled.
- [ ] Revalidate all assumptions immediately before execution.
- [ ] Unit-test races by mutating a snapshot between plan and validation.

Exit criterion: dry-run output explains exactly what would move and why other items were skipped.

### Milestone 4 — chest auto-stack execution

Goal: execute only through authoritative vanilla state-change paths.

- [ ] Implement one centralized executor.
- [ ] Add abort/recompute behavior for stale plans.
- [ ] Verify inventory/container change notifications and network synchronization.
- [ ] Test permission denial, chest-in-use, disconnect, destroyed target, and simultaneous clients.
- [ ] Decide whether vanilla paths are sufficient or Jötunn/custom RPC/server code is required.
- [ ] State the tested install contract explicitly.

Exit criterion: full local/peer-hosted/dedicated data-integrity matrix passes.

### Milestone 5 — configuration and UX polish

- [ ] Name and group settings clearly.
- [ ] Supply conservative defaults.
- [ ] Make invalid values clamp or fail safely with a clear log.
- [ ] Add keyboard/mouse and controller behavior required for supported platforms.
- [ ] Add concise HUD feedback without layout spam.
- [ ] Test live config reload only for settings that can safely change at runtime.
- [ ] Document generated config location and each option.

### Milestone 6 — package candidate

- [ ] Finalize team/package/plugin identity.
- [ ] Create 256×256 `icon.png`.
- [ ] Write package README and changelog.
- [ ] Pin live dependency versions.
- [ ] Build one exact ZIP.
- [ ] Run automated structural checks.
- [ ] Preinstall declared dependencies, then import the ZIP into a brand-new r2modman profile; local imports do not resolve dependencies automatically.
- [ ] Complete uninstall/reinstall/regression tests.
- [ ] Archive the tested ZIP and its source commit locally.

Exit criterion: the byte-for-byte package candidate has passed the release matrix.

### Milestone 7 — manual first release

- [ ] Joe reviews name, description, compatibility claims, icon, and changelog.
- [ ] Validate `manifest.json` with Thunderstore's current validator.
- [ ] Preview README with Thunderstore's current Markdown preview.
- [ ] Re-check current Valheim community categories/disclosure rules.
- [ ] Upload manually to the chosen team.
- [ ] After manager-cache propagation, install the published version through r2modman in a clean profile and verify declared dependencies auto-install.
- [ ] Verify startup and one core workflow from the public package.
- [ ] Tag the exact source commit only after publication is confirmed.

No automated publishing until the manual release process is proven and Joe asks for it.

## 6. Test plan

The detailed matrix is in [RESEARCH.md](RESEARCH.md#11-required-test-matrix). Each test run should record:

```text
Date/time:
Source commit:
Package version:
Package ZIP checksum:
Valheim version:
BepInEx pack/framework version:
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

Minimum release gates:

- [ ] vanilla and BepInEx-only baselines pass;
- [ ] clean-profile startup has no plugin exceptions;
- [ ] item counts and metadata are conserved in every mutation test;
- [ ] local inventory sort passes repeated runs;
- [ ] each claimed multiplayer combination is tested, not inferred;
- [ ] version mismatch behavior is understood;
- [ ] uninstall leaves worlds and characters loadable;
- [ ] controller/platform claims are actually tested;
- [ ] exact ZIP imports via r2modman;
- [ ] exact ZIP contains no game assemblies, local paths, credentials, or development debris.

## 7. Package/release automation to build later

`verify-package.ps1` should fail on:

- missing root `manifest.json`, `README.md`, or `icon.png`;
- invalid JSON or missing manifest fields;
- invalid package name/version/description length;
- placeholder dependency versions;
- icon not exactly 256×256 PNG;
- unexpected root directory nesting;
- included local path/property files;
- included game/BepInEx/Harmony/Jötunn DLLs unless deliberately licensed and required;
- PDBs or test assemblies in a release build unless intentionally selected;
- package manifest/plugin/assembly version mismatch.

`package.ps1` should:

1. clean a staging directory;
2. copy only an allowlist of package metadata and runtime outputs;
3. inject one validated version into all generated surfaces;
4. run the verifier;
5. create a deterministic ZIP under `artifacts/`;
6. print the artifact path and checksum;
7. never upload.

Publishing should remain a separate, explicit command so building cannot accidentally release.

## 8. Branching/splitting criteria

Keep the combined package while:

- both features share the same required install locations;
- both can coexist behind config flags;
- they release and break together;
- the combined description remains understandable.

Split into `InventorySorting` and `ChestAutoStack` packages when any of these becomes true:

- one is client-only and the other requires server/client installation;
- one adopts Jötunn or another dependency while the other does not;
- users should be able to update one without accepting risk in the other;
- patches conflict with different classes of mods;
- separate maintainers/release cadences emerge;
- package configuration becomes confusing despite grouping.

Before a split:

- assign new stable plugin GUIDs and package identities;
- preserve or explicitly migrate config values;
- prevent both old combined and new split packages from loading together silently;
- document the migration path and incompatibility;
- test upgrade, downgrade where supported, and clean uninstall.

## 9. Immediate next conversation

The fastest productive follow-up is to settle the first-release behavior, especially:

1. exact sort order and protected slots;
2. whether chest auto-stack fills existing stacks only or may use empty slots;
3. trigger style and radius;
4. supported container types;
5. controller requirement;
6. desired first-release name.

Once those are decided, validate Joe's installed Valheim/r2modman versions and local assembly layout before generating C# project files.

## Current state

- Research and planning only.
- Repository initialized and versioned.
- No SDK/compiler installed.
- No game or profile files accessed.
- No Valheim installation modified.
- No Thunderstore account/team/token accessed.
- No package built or published.
