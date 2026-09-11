# Valheim 1.0 Modding Research

- **Research date:** 2026-09-10
- **Project:** configurable inventory and storage quality-of-life mods
- **Distribution:** Thunderstore
- **Installation and test manager:** r2modman

> **Status note (2026-09-11):** This document preserves the pre-design ecosystem research. Product choices and release gates are now settled in [DESIGN-QUESTIONNAIRE.md](DESIGN-QUESTIONNAIRE.md), and the reconciled implementation sequence is in [NEXT-STEPS.md](NEXT-STEPS.md). Those documents override any earlier option or broader test suggestion below.

## Executive recommendation

Start with **one deployable BepInEx 5 plugin and one Thunderstore package** for the first inventory/storage feature set. Keep inventory sorting, protection, and the deposit/replenish action as separate internal modules for testing and maintenance. Do not create a separately installed shared library yet.

Use **plain BepInEx + its bundled HarmonyX** unless implementation proves that Jötunn provides something the first release actually needs. Jötunn is viable and has already published a Valheim 1.0 compatibility update, but a vanilla-code inventory plugin does not automatically need its larger runtime dependency.

The largest design risk is not packaging; it is the multiplayer correctness of moving items into networked containers. Implement pure sorting first, then add chest mutation only after inspecting Valheim 1.0.7's actual inventory/container paths and proving behavior in a host/client matrix.

## Confidence legend

- **Verified** — stated by a current first-party/upstream source, or directly observed in this repository.
- **Provisional** — likely and suitable for planning, but must be checked against Joe's installed Valheim/r2modman environment.
- **Stale or uncertain** — older guidance or an ecosystem assumption that must not be treated as a current contract.

## 1. Current ecosystem state

### Valheim 1.0 is a fresh compatibility boundary

**Verified:** Valheim 1.0 launched on September 9, 2026. The official launch notes list a Unity engine upgrade, inventory-size upgrades, a new save system, and many other changes. All are reasons to assume old method signatures and patches may have changed.

- [Official Valheim 1.0 launch and patch notes](https://store.steampowered.com/news/app/892970/view/692020124159311902)

**Verified:** Jötunn's current Thunderstore changelog lists version 2.30.0 as updating “the majority of systems for Valheim 1.0.7.” It also says piece categories were not yet fully updated. This is useful evidence that the community stack is actively adapting, not evidence that every mod or API is stable.

- [Jötunn Thunderstore changelog](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/changelog/)

**Provisional:** Use **Valheim 1.0.7** as the present compatibility target named by Jötunn, but record and verify the exact version displayed by Joe's installed game at the start of development and in every test report.

**Stale or uncertain:** Pre-1.0 tutorials can still explain concepts, but their assembly names, game methods, target frameworks, and screenshots may no longer match the live game. Valheim modding remains unofficial, so a game update can invalidate runtime patches.

## 2. Recommended runtime stack

### BepInEx 5

**Verified:** The maintained Valheim Thunderstore pack is a customized, preconfigured BepInEx 5 package. On the research date it reported BepInEx **5.4.23.5**. Its package page describes plugin loading, runtime/assembly patching, configuration, logging, dependency handling, a Valheim-specific entry point, and client/dedicated-server launch support. The pack supports Windows and Linux; its current notes say native Apple Silicon injection remains unsupported, so an M-series Mac requires Rosetta.

- [BepInExPack Valheim on Thunderstore](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim)
- [BepInEx documentation](https://github.com/bepinex/bepinex-docs/blob/HEAD/index.md)
- [Basic plugin tutorial](http://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/)
- [Logging tutorial](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/3_logging.html)
- [Configuration tutorial](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/4_configuration.html)

Use the current Valheim-specific Thunderstore pack rather than a generic GitHub BepInEx archive. The pack's Thunderstore package version and its embedded upstream BepInEx version are separate concepts, so copy the exact dependency string from the live package page when producing a release instead of deriving it from `5.4.23.5`.

Expected profile locations:

```text
<r2modman profile>/
└── BepInEx/
    ├── config/
    ├── core/
    │   ├── BepInEx.dll (name to verify)
    │   └── 0Harmony.dll
    ├── plugins/
    └── LogOutput.log
```

Use `Config.Bind` for settings, the plugin logger for diagnostics, and `BepInEx.Paths` when code needs a BepInEx-managed location. Do not hard-code Windows profile paths.

### HarmonyX

**Verified:** BepInEx uses HarmonyX for runtime method patching.

- [HarmonyX repository](https://github.com/BepInEx/HarmonyX)

Reference the `0Harmony.dll` supplied by the active BepInEx profile. Do not introduce a second Harmony package unless a demonstrated incompatibility requires it.

Patch policy:

1. Prefer a public game/API path without patching when one exists.
2. Prefer narrow Prefix/Postfix patches.
3. Keep each patch adjacent to the feature that owns it.
4. Avoid broad stateful hooks and per-frame work.
5. Use transpilers only when there is no safer seam; they are the most update-fragile and conflict-prone option.
6. Log patch failures clearly at startup and disable only the affected feature where possible.

### BepInEx 6

**Verified:** BepInEx 6 documentation is still marked work-in-progress. The Valheim community package remains a BepInEx 5 line.

**Recommendation:** do not begin with a speculative BepInEx 6 migration.

## 3. Jötunn: useful, but not mandatory

Jötunn provides higher-level managers and utilities for prefabs, assets, localization, GUI, inputs, configuration synchronization, network compatibility enforcement, assembly publicizing, and release automation.

Current upstream references:

- [Jötunn repository](https://github.com/Valheim-Modding/Jotunn)
- [Jötunn Mod Stub](https://github.com/Valheim-Modding/JotunnModStub)
- [Jötunn example mod](https://github.com/Valheim-Modding/JotunnModExample)
- [Step-by-step guide](https://github.com/valheim-modding/jotunn/blob/HEAD/JotunnLib/Documentation/guides/guide.md)
- [Quickstart](https://github.com/valheim-modding/jotunn/blob/HEAD/JotunnLib/Documentation/guides/quickstart.md)
- [Network compatibility](https://github.com/valheim-modding/jotunn/blob/HEAD/JotunnLib/Documentation/tutorials/networkcompatibility.md)

### Decision for the first plugin

Start without Jötunn if the mod only:

- reads and reorders vanilla inventory slots;
- invokes vanilla container/inventory methods;
- uses BepInEx configuration and logging;
- uses no custom assets, prefabs, items, RPCs, or synchronized configuration.

Adopt Jötunn if implementation needs:

- its network compatibility/version enforcement;
- synchronized server-owned configuration;
- custom RPCs or managers;
- assets, prefabs, localization, or richer input/UI helpers;
- its publicizer/prebuild workflow after that workflow is validated on the installed 1.0 build.

This keeps the first dependency graph small while leaving a clear migration path. Jötunn 2.30.0's 1.0.7 work makes it a credible option, but its own changelog says not every system is finished.

## 4. C#, target framework, and references

### What is known

**Verified locally:** this workspace currently has no `dotnet`, `mono`, `csc`, or `mcs` executable installed. No software was installed during this research pass.

**Verified upstream:** the Valheim community wiki's raw-BepInEx setup recommends a .NET Framework 4.8 class library, while Jötunn's current getting-started guide still instructs developers to install the .NET Framework 4.6.2 targeting pack. The raw-BepInEx wiki says it was verified against Valheim 0.219.16, so its API examples predate 1.0.

- [Valheim community: creating your first mod](https://github.com/Valheim-Modding/Wiki/wiki/Creating-Your-First-Mod)
- [Valheim community: setting up a development environment](https://github.com/Valheim-Modding/Wiki/wiki/Setting-Up-Mod-Development-Environment)

### What must be verified before scaffolding

Do not lock the project target framework from this document alone. First inspect:

1. the current Jötunn Mod Stub project file if choosing Jötunn;
2. the installed Valheim 1.0.7 managed assemblies;
3. the active BepInEx pack's assemblies and supported compiler/runtime;
4. one current, proven Valheim 1.0 plugin source project if available.

`.NET Framework 4.8` is the documented raw-BepInEx candidate and `4.6.2` is the documented Jötunn candidate. Neither is yet a repository decision; preserve the current template's settings rather than forcing a framework from an older tutorial.

### Assembly references

Expected local reference sources, with filenames to verify:

- `<profile>/BepInEx/core` for BepInEx and `0Harmony.dll`;
- `<Valheim>/Valheim_Data/Managed` for Valheim and Unity managed assemblies;
- a generated/publicized Valheim assembly only if private-member access is unavoidable.

Keep all copied game assemblies out of git and out of release ZIPs. They are local dependencies and copyrighted game material. Use an ignored local property file such as `Environment.props` or `Directory.Build.props.local` for machine-specific paths.

**Stale or uncertain:** older examples often name `assembly_valheim.dll` and `assembly_valheim_publicized.dll`. Do not assume those filenames or publicized-member behavior survived the 1.0 engine update. Inspect the local directories.

## 5. Plugin structure and lifecycle

The first assembly should have:

- one stable, reverse-domain plugin GUID;
- one user-facing name;
- one version value shared with the package manifest;
- a small `BaseUnityPlugin` entry point;
- feature-owned initialization and teardown;
- configuration bound once, with change handling where safe;
- a single Harmony instance owned by the plugin;
- startup logging with plugin version, game version, detected dependencies, and compatibility mode.

Avoid static global state where possible. Core sorting and transfer planning should be plain deterministic C# independent of Unity classes, making it unit-testable without launching the game.

## 6. First-mod functional boundaries

### Inventory sorting

Potentially client-only if it only reorders the local player's own inventory through normal game methods. Still test host and guest roles, because player inventory is saved and synchronized through game systems.

Recommended deterministic sort key:

1. protected/pinned slot remains untouched;
2. configurable item-category priority;
3. localized or internal item name, chosen explicitly;
4. quality;
5. durability or another stable tie-breaker;
6. original slot index as final stable tie-breaker.

Do not merge stacks or mutate metadata merely as a side effect of sorting unless that behavior is separately enabled and tested.

### Chest auto-stack

Treat this as network-affecting. It changes shared container state even if the trigger is a client hotkey. The correct implementation must respect Valheim's current container ownership, permissions, distance checks, and synchronization paths.

Do not label it “client-only” until tests prove:

- a modded guest can perform the operation against a vanilla host/server without divergence;
- a vanilla peer immediately sees the correct result;
- disconnects and simultaneous access do not lose or duplicate items;
- wards/access rules and active-container ownership are respected.

If custom RPCs, server enforcement, shared config, or custom content are introduced, expect the mod to be required on the server and some or all clients.

Jötunn exposes the following network-compatibility concepts: `NotEnforced`, `VersionCheckOnly`, `EveryoneMustHaveMod`, `ClientMustHaveMod`, and `ServerMustHaveMod`. Choose a mode only after the implementation's actual contract is known.

### Safe transfer model

Use a transaction-like plan/validate/execute flow:

1. Snapshot eligible source stacks and reachable target containers.
2. Exclude protected, equipped, hotbar, favorited, quest, or otherwise unsafe items.
3. Build a deterministic move plan against available compatible partial stacks first.
4. After every compatible partial stack is full, consider empty slots only in containers that already hold a compatible item, following the approved targeted-chest then nearest-to-farthest routing.
5. Revalidate source amount, target capacity, access, range, and ownership immediately before each mutation.
6. Use vanilla inventory/container mutation and network-notification paths.
7. Abort or recompute on stale state; never “force” the expected state.
8. Report a concise result: moved item count, touched containers, and skipped reasons.

Preserve item identity and metadata, including quality, durability, custom data, world level, stack limit, and any mod-added fields. Never rebuild an item from only its prefab/name when moving an existing stack.

## 7. Configuration design

The approved v0.1 surface contains exactly three profile-wide settings:

- auto-sort, edited through the inventory checkbox and enabled by default;
- nearby-storage radius, exposed through BepInEx/r2modman and defaulting to 20 meters;
- the deposit/replenish keybind, exposed through BepInEx/r2modman and defaulting to Left Alt + E.

Do not add master-enable, feature-toggle, preset, filter, verbosity, per-container, or advanced settings in v0.1. Diagnostic detail may use ordinary BepInEx logging without becoming a public configuration option.

Protected exact slots and optional replenishment targets are character data rather than profile settings; they follow that character across worlds. Use a small versioned persistence schema, validate it before use, and fail without moving items if it is malformed or incompatible.

## 8. Logging and debugging

### Logging

Use the BepInEx logger. `BepInEx/LogOutput.log` in the active r2modman profile is the first diagnostic artifact.

At startup, log once:

- plugin GUID and version;
- detected game version;
- BepInEx/Harmony/Jötunn versions when available;
- enabled features;
- declared network mode.

For each user-triggered operation, log a concise summary at Info or Debug level. Put detailed skip reasons behind diagnostic logging. Never log every frame and never dump full inventories unless a temporary developer build explicitly enables bounded diagnostics.

### Debugging sequence

1. Reproduce on a backed-up disposable character/world.
2. Reduce to a clean r2modman profile with BepInEx and this plugin.
3. Read `LogOutput.log` from startup through the failure.
4. Confirm all patches applied to the exact supported Valheim build recorded from the installed game.
5. Reproduce with deterministic inventory/container contents.
6. Add narrow diagnostic logs around the failing state transition.
7. Inspect/decompile the locally installed game assemblies as needed.
8. Only then test conflicts with other inventory mods.

An older IDE-attach guide exists, but it is invasive and may be stale after the Unity upgrade:

- [Debugging plugins via IDE](https://github.com/Valheim-Modding/Wiki/wiki/Debugging-Plugins-via-IDE)

Do not modify Joe's game executable or managed assemblies for debugger support without a separate explicit request and backups.

## 9. r2modman development and test workflow

Create a dedicated clean development profile. Install only the current Valheim BepInEx pack initially. Use **Settings → Browse profile folder** to obtain the actual profile directory; do not assume an AppData path.

Two useful deployment loops:

### Fast development loop

Copy the compiled DLL and its required non-framework assets into a namespaced folder under:

```text
<profile>/BepInEx/plugins/<PluginName>/
```

Launch with r2modman's modded launch action and inspect `BepInEx/LogOutput.log`.

### Release-representative loop

1. Build the exact Thunderstore ZIP.
2. Preinstall its declared dependencies in the test profile.
3. Import the ZIP using r2modman's local-mod import action under profile settings.
4. Confirm the installed paths.
5. Launch modded from the clean profile.
6. Verify a clean launch, intended defaults, and the representative core workflow.

The exact UI label has appeared as **Import Local Mod** in current community examples, but wording may vary by r2modman release. Local imports do not automatically install dependencies, receive updates, or appear in exported profiles; those behaviors must be tested separately with the published package.

## 10. Multiplayer compatibility model

Document one of these contracts for every released feature:

| Behavior | Likely installation contract | Status |
| --- | --- | --- |
| Local UI/button only | Client only | Provisional |
| Sort local player inventory through vanilla paths | Client only, host/guest tested | Provisional |
| Move items into shared containers through vanilla ownership/RPC paths | Possibly client only, but network-affecting | Must be proven |
| Custom RPC or server-authoritative policy | Server and relevant clients | Expected |
| Synced configuration/version enforcement | Server and clients per selected mode | Expected |
| Custom prefabs/items/content | All peers that must understand the content | Expected |

Do not use “works in multiplayer” without recording the exact combinations tested.

Console crossplay does not make PC plugin DLLs loadable on consoles. If unmodded console clients must join, preserve vanilla-compatible data and wire behavior; treat custom-content and custom-RPC designs as requiring mod-capable clients unless testing proves otherwise.

**Unverified:** some community discussions claim broader crossplay limitations for BepInEx/modded servers. No sufficiently current upstream statement was established in this research pass. If crossplay matters, test the real deployment rather than copying a blanket claim.

## 11. Approved v0.1 test matrix

Use backed-up disposable worlds and characters. The exact release gate is maintained in [NEXT-STEPS.md](NEXT-STEPS.md); the condensed requirements are:

### Automated coverage

- deterministic sorting and compatible stack consolidation;
- exact protected-slot behavior;
- partial-stack priority, targeted-chest priority, nearest routing, and stable slot order;
- replenishment, partial shortages, excess-above-target handling, overflow, and no destination;
- item-count and metadata conservation;
- safe time-budget exhaustion and incomplete-search feedback;
- compatibility-check failure disabling all item-changing behavior.

### Gameplay smoke environments

- solo;
- co-op host with an unmodded peer;
- co-op guest of a vanilla host;
- modded client on a vanilla dedicated server with no server-side Stackmaster installation.

Each multiplayer run checks peer compatibility and synchronized container state. Any observed item loss, duplication, crash, corrupt metadata, or synchronization error blocks release.

### Additional release gates

- clean profile containing only BepInEx, Stackmaster, and declared dependencies;
- one complete quit/relaunch with the same character to verify protected-slot/target persistence;
- one normal-sized base at the 20-meter default to verify responsiveness and profile the internal time budget;
- fresh r2modman import of the exact ZIP with intended defaults;
- package structure validation and exclusion of local paths, game DLLs, credentials, test binaries, and development debris.

A deliberately oversized stress base, other-mod conflicts, controller support, upgrade behavior, clean removal, cross-world persistence, and separate-character isolation are outside the v0.1 release gate. Broader testing can still be added when evidence warrants it, but must not silently replace the approved definition of done.

## 12. Thunderstore package format

Official references:

- [Creating a package](https://wiki.thunderstore.io/mods/creating-a-package)
- [Updating a package](https://wiki.thunderstore.io/mods/updating-a-package)
- [Manifest v1 validator](https://thunderstore.io/tools/manifest-v1-validator)
- [Markdown preview](https://thunderstore.io/tools/markdown-preview/)
- [Thunderstore CLI wiki](https://github.com/thunderstore-io/thunderstore-cli/wiki)

Required ZIP-root files:

```text
icon.png       # exactly 256 × 256 PNG
README.md      # UTF-8
manifest.json  # UTF-8
```

Optional but recommended:

```text
CHANGELOG.md
```

Recommended plugin payload:

```text
plugins/
└── Stackmaster/
    └── Stackmaster.dll
```

Example manifest shape — placeholders are intentional:

```json
{
  "name": "Stackmaster",
  "version_number": "0.1.0",
  "website_url": "",
  "description": "Turn a messy Viking inventory into a tidy, adventure-ready loadout.",
  "dependencies": [
    "denikson-BepInExPack_Valheim-<verify-current-version>"
  ]
}
```

Rules worth encoding in a verification script:

- package name uses letters, digits, and underscores and is at most 128 characters;
- version is numeric `major.minor.patch`, without a prerelease suffix;
- description is at most 250 characters;
- each dependency is `{team}-{package}-{version}`;
- `icon.png` is exactly 256×256;
- required files are at ZIP root;
- archive the root contents, not their containing directory;
- only intended runtime files are present.

Pin dependency versions in each release artifact. Re-check the current live version immediately before packaging.

## 13. Publishing and identity

Manual upload is the safest first-release path.

Approved identity before publishing:

- Thunderstore team: `JStack424`;
- permanent package name: `Stackmaster`, unless the final creation flow reveals a collision;
- BepInEx GUID: `com.jstack424.stackmaster`;
- license: MIT;
- public GitHub source with Issues enabled, using that repository as the Thunderstore homepage;
- original square chest-stack icon direction and the approved public lead;
- actual compatibility claims remain limited to the environments that pass the release matrix.

The exact GitHub owner/remote creation and the final publication action still require Joe's approval.

Team plus package name becomes the durable package identity. Published versions are immutable; even a README correction requires a new version. Thunderstore displays the highest semantic version, not necessarily the latest upload by date. Never reuse an existing version number for a changed ZIP.

TCLI can automate validation/build/publishing later, but its own wiki marks it pre-release. If adopted, pin a reviewed version. Publishing uses a Thunderstore service-account token via a protected environment variable or explicit secure argument. Never commit or log it.

Verify the live Valheim community's categories and AI-content disclosure rules immediately before first upload. Do not infer current moderation requirements from stale screenshots.

No account, team, token, or package was created during this research, and nothing was uploaded.

## 14. Versioning and update policy

Use one version source to drive:

- assembly metadata;
- `BepInPlugin` version;
- `manifest.json`;
- package filename;
- changelog heading.

Suggested policy:

- begin at `0.1.0` while behavior and multiplayer compatibility are unsettled;
- patch: fixes with no intended compatibility-contract change;
- minor: additive behavior/configuration; require coordinated upgrades if protocol/content changes;
- major: breaking config, API, saved-data, or compatibility change.

After every Valheim, BepInEx pack, HarmonyX, or Jötunn update:

1. rebuild against the current local assemblies;
2. run clean-profile startup and patch checks;
3. run the relevant local and multiplayer matrix;
4. rebuild and verify the exact release ZIP;
5. publish only with explicit approval.

Do not auto-publish initially.

## 15. Sources and freshness notes

The most decision-relevant sources are the official Valheim launch notes, the current Thunderstore package/changelog pages, BepInEx's documentation, Jötunn's current repository/docs, and Thunderstore's official package documentation.

Public search caches can lag live package pages and sometimes display confusing “last updated” metadata. Package versions in this document are evidence of current ecosystem activity, not values to hard-code. Release scripts must read a deliberately updated source value and validation must happen immediately before upload.
